using System.Reflection;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Neominal.Microservices.Template.Endpoints;
using Neominal.Microservices.Template.Infrastructure;
using Neominal.Microservices.Template.Middleware;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

const string ServiceName = "neominal-microservices-template";

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// 1) SERILOG - Merkezi / Structured Logging
// =====================================================================
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", ServiceName)
        .WriteTo.Console()
        .WriteTo.Seq(
            context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341",
            apiKey: string.IsNullOrWhiteSpace(context.Configuration["Seq:ApiKey"])
                ? null
                : context.Configuration["Seq:ApiKey"]);
});

// =====================================================================
// 2) OPENTELEMETRY - Distributed Tracing + Metrics
// =====================================================================
var otlpEndpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

// =====================================================================
// 3) POSTGRESQL / EF CORE
// =====================================================================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// =====================================================================
// 4) REDIS
// =====================================================================
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

// =====================================================================
// 5) KAFKA
// =====================================================================
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddSingleton<ReceivedMessagesStore>();
builder.Services.AddHostedService<KafkaConsumerHostedService>();

// =====================================================================
// 5b) RABBITMQ
// =====================================================================
builder.Services.AddSingleton<IConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var factory = new ConnectionFactory
    {
        HostName = configuration["RabbitMq:Host"] ?? "localhost",
        Port = int.Parse(configuration["RabbitMq:Port"] ?? "25672"),
        UserName = configuration["RabbitMq:Username"] ?? "neominal",
        Password = configuration["RabbitMq:Password"] ?? "neominal_pass"
    };

    // RabbitMQ uygulama acilisinda henuz tam hazir olmayabilir; birkac
    // deneme ile bekleyerek baglaniyoruz (Kafka topic olusturmadaki ile
    // ayni mantik).
    const int maxAttempts = 5;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            return factory.CreateConnection();
        }
        catch (Exception) when (attempt < maxAttempts)
        {
            Thread.Sleep(3000);
        }
    }

    return factory.CreateConnection();
});

builder.Services.AddSingleton<IRabbitMqPublisherService, RabbitMqPublisherService>();
builder.Services.AddSingleton<RabbitMqReceivedMessagesStore>();
builder.Services.AddHostedService<RabbitMqConsumerHostedService>();

// =====================================================================
// 6) VAULT
// =====================================================================
builder.Services.AddSingleton<IVaultSecretService, VaultSecretService>();

// =====================================================================
// 7) VALIDASYON (FluentValidation)
// =====================================================================
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// =====================================================================
// 8) GLOBAL EXCEPTION HANDLING
// =====================================================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// =====================================================================
// 9) HEALTH CHECKS (Postgres + Redis)
// =====================================================================
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgres")
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379",
        name: "redis");

// =====================================================================
// 10) SWAGGER
// =====================================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =====================================================================
// 11) HANGFIRE - Background Jobs (PostgreSQL storage)
// =====================================================================
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        builder.Configuration.GetConnectionString("Postgres"),
        new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

builder.Services.AddHangfireServer();
builder.Services.AddScoped<DemoJobService>();

var app = builder.Build();

// ---------------------------------------------------------------------
// Middleware pipeline sırası: Exception Handling -> Logging -> Endpoints
// ---------------------------------------------------------------------
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint(); // /metrics

// ---------------------------------------------------------------------
// Hangfire Dashboard - /hangfire
// UYARI: Bu dashboard demo/egitim amacli acik (auth'suz) birakilmistir.
// Gercek bir prod ortaminda mutlaka bir IDashboardAuthorizationFilter
// ile korunmalidir (ornegin sadece admin rolune izin verecek sekilde).
// ---------------------------------------------------------------------
app.UseHangfireDashboard("/hangfire");

app.MapDemoEndpoints();
app.MapJobsEndpoints();
app.MapRabbitMqEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = ServiceName,
    status = "up",
    scenarios = new[]
    {
        "GET  /health                          -> Postgres + Redis saglik kontrolu",
        "GET  /metrics                         -> Prometheus scrape endpoint",
        "POST /demo/db/products                -> PostgreSQL'e kayit ekleme",
        "GET  /demo/db/products                -> PostgreSQL'den kayit okuma",
        "GET  /demo/errors/{type}              -> Global exception handling (not-found|unauthorized|bad-request)",
        "GET  /demo/cache/{key}                -> Redis cache-aside senaryosu",
        "POST /demo/kafka/publish              -> Kafka'ya mesaj yayinlama",
        "GET  /demo/kafka/messages             -> Kafka'dan tuketilen mesajlar",
        "POST /demo/rabbitmq/publish           -> RabbitMQ'ya mesaj yayinlama",
        "GET  /demo/rabbitmq/messages          -> RabbitMQ'dan tuketilen mesajlar",
        "GET  /demo/vault/secret/{path}        -> Vault'tan secret okuma",
        "POST /demo/vault/secret/{path}        -> Vault'a secret yazma",
        "GET  /hangfire                        -> Hangfire Dashboard (background job'lari izleme)",
        "POST /demo/jobs/fire-and-forget       -> Aninda kuyruga alinan job",
        "POST /demo/jobs/schedule              -> Gecikmeli (scheduled) job",
        "POST /demo/jobs/recurring             -> Cron ile tekrarlayan job olusturma/guncelleme",
        "DELETE /demo/jobs/recurring           -> Tekrarlayan job'i kaldirma"
    }
}));

// ---------------------------------------------------------------------
// Basitlik icin EnsureCreated kullanildi. Gercek bir prod projede
// "dotnet ef migrations add" + "dotnet ef database update" kullanin.
// ---------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// ---------------------------------------------------------------------
// Uygulama, kendi Kafka topic'ini (dogru partition/replication factor
// ile) acilista kendisi olusturur; Kafka'nin auto-create ayarina
// bagimli kalinmaz.
// ---------------------------------------------------------------------
await KafkaTopicInitializer.EnsureTopicsExistAsync(app.Configuration, app.Logger);

app.Run();
