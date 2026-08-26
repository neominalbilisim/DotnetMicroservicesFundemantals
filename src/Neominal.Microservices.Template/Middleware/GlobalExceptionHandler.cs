using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Neominal.Microservices.Template.Middleware;

/// <summary>
/// Senaryo: Cross-cutting concern olarak merkezi hata yönetimi.
/// Tüm yakalanmamış exception'lar burada ele alınır, loglanır ve
/// RFC 7807 (ProblemDetails) standardında bir response'a dönüştürülür.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Yakalanmamış hata oluştu: {Message}", exception.Message);

        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Geçersiz istek"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Kayıt bulunamadı"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Yetkisiz erişim"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir sunucu hatası oluştu")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
