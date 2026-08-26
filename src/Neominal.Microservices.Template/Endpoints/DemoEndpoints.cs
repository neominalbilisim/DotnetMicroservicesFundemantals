using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Neominal.Microservices.Template.Infrastructure;
using Neominal.Microservices.Template.Validation;
using StackExchange.Redis;

namespace Neominal.Microservices.Template.Endpoints;

public record KafkaPublishRequest(string Message);

public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/demo").WithTags("Demo Senaryolari");

        // -----------------------------------------------------------
        // 1) PostgreSQL senaryosu (EF Core + FluentValidation)
        // -----------------------------------------------------------
        group.MapPost("/db/products", async (
            CreateProductRequest request,
            IValidator<CreateProductRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var product = new Product { Name = request.Name, Price = request.Price };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            return Results.Created($"/demo/db/products/{product.Id}", product);
        });

        group.MapGet("/db/products", async (AppDbContext db) =>
            Results.Ok(await db.Products
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(20)
                .ToListAsync()));

        // -----------------------------------------------------------
        // 2) Global Exception Handling senaryosu
        // -----------------------------------------------------------
        group.MapGet("/errors/{type}", (string type) =>
        {
            throw type switch
            {
                "not-found" => new KeyNotFoundException("Aranan kayit bulunamadi."),
                "unauthorized" => new UnauthorizedAccessException("Bu islem icin yetkiniz yok."),
                "bad-request" => new ArgumentException("Gonderilen veri gecersiz."),
                _ => new InvalidOperationException("Beklenmeyen bir sunucu hatasi olustu.")
            };
        });

        // -----------------------------------------------------------
        // 3) Redis cache-aside senaryosu
        // -----------------------------------------------------------
        group.MapGet("/cache/{key}", async (string key, IConnectionMultiplexer redis) =>
        {
            var db = redis.GetDatabase();
            var cached = await db.StringGetAsync(key);

            if (cached.HasValue)
            {
                return Results.Ok(new { source = "redis-cache", key, value = cached.ToString() });
            }

            var freshValue = $"deger-{Guid.NewGuid():N}";
            await db.StringSetAsync(key, freshValue, TimeSpan.FromMinutes(1));

            return Results.Ok(new { source = "generated", key, value = freshValue });
        });

        // -----------------------------------------------------------
        // 4) Kafka publish / consume senaryosu
        // -----------------------------------------------------------
        group.MapPost("/kafka/publish", async (KafkaPublishRequest request, IKafkaProducerService producer) =>
        {
            await producer.PublishAsync(KafkaConsumerHostedService.Topic, request.Message);
            return Results.Accepted(value: new { status = "published", request.Message });
        });

        group.MapGet("/kafka/messages", (ReceivedMessagesStore store) =>
            Results.Ok(store.GetAll()));

        // -----------------------------------------------------------
        // 5) HashiCorp Vault senaryosu
        // -----------------------------------------------------------
        group.MapGet("/vault/secret/{*path}", async (string path, IVaultSecretService vault) =>
        {
            var secret = await vault.GetSecretAsync(path);
            return Results.Ok(secret);
        });

        group.MapPost("/vault/secret/{*path}", async (
            string path,
            Dictionary<string, object> data,
            IVaultSecretService vault) =>
        {
            await vault.WriteSecretAsync(path, data);
            return Results.Ok(new { status = "written", path });
        });
    }
}
