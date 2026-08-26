namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Senaryo: Hangfire background job'larinin gercekte calistirdigi is mantigi.
/// Hangfire, job tetiklendiginde bu servisi DI container'indan (Hangfire.AspNetCore
/// otomatik olarak ASP.NET Core'un kendi service provider'ini kullanir) resolve eder.
/// </summary>
public class DemoJobService
{
    private readonly ILogger<DemoJobService> _logger;

    public DemoJobService(ILogger<DemoJobService> logger)
    {
        _logger = logger;
    }

    public void Execute(string message)
    {
        _logger.LogInformation("[Hangfire Job] {Message} - Calisma zamani (UTC): {Time}", message, DateTime.UtcNow);
    }
}
