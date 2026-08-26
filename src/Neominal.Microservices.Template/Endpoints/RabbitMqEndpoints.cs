using Neominal.Microservices.Template.Infrastructure;

namespace Neominal.Microservices.Template.Endpoints;

public record RabbitMqPublishRequest(string Message);

public static class RabbitMqEndpoints
{
    public static void MapRabbitMqEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/demo/rabbitmq").WithTags("Demo Senaryolari - RabbitMQ");

        group.MapPost("/publish", (RabbitMqPublishRequest request, IRabbitMqPublisherService publisher) =>
        {
            publisher.Publish(request.Message);
            return Results.Accepted(value: new { status = "published", request.Message });
        });

        group.MapGet("/messages", (RabbitMqReceivedMessagesStore store) =>
            Results.Ok(store.GetAll()));
    }
}
