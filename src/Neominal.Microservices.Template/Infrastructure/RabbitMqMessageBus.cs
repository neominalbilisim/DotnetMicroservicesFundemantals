using System.Collections.Concurrent;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Senaryo: RabbitMQ'ya mesaj publish etme ve arka planda tuketme.
/// Kafka senaryosuyla paralel bir yapi kurulmustur: ayni sekilde
/// bir publisher, bellek ici bir mesaj deposu ve bir background consumer.
/// </summary>
public interface IRabbitMqPublisherService
{
    void Publish(string message);
}

public class RabbitMqPublisherService : IRabbitMqPublisherService, IDisposable
{
    public const string QueueName = "demo-queue";

    private readonly IModel _channel;

    public RabbitMqPublisherService(IConnection connection)
    {
        _channel = connection.CreateModel();
        _channel.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false);
    }

    public void Publish(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true; // mesaj RabbitMQ restart'ta da kaybolmasin

        _channel.BasicPublish(exchange: "", routingKey: QueueName, basicProperties: properties, body: body);
    }

    public void Dispose() => _channel?.Close();
}

/// <summary>
/// Tuketilen son N mesaji bellekte tutan basit bir depo (demo amacli).
/// </summary>
public class RabbitMqReceivedMessagesStore
{
    private readonly ConcurrentQueue<string> _messages = new();
    private const int MaxItems = 50;

    public void Add(string message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > MaxItems && _messages.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyCollection<string> GetAll() => _messages.ToArray();
}

public class RabbitMqConsumerHostedService : BackgroundService
{
    private readonly IConnection _connection;
    private readonly RabbitMqReceivedMessagesStore _store;
    private readonly ILogger<RabbitMqConsumerHostedService> _logger;
    private IModel? _channel;

    public RabbitMqConsumerHostedService(
        IConnection connection,
        RabbitMqReceivedMessagesStore store,
        ILogger<RabbitMqConsumerHostedService> logger)
    {
        _connection = connection;
        _store = store;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(queue: RabbitMqPublisherService.QueueName, durable: true, exclusive: false, autoDelete: false);

        // Adil dagitim: consumer bir sonraki mesaji, oncekini ack'lemeden almaz.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (_, ea) =>
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("RabbitMQ mesaji tuketildi: {Message}", message);
            _store.Add(message);

            // Manuel ack: mesaj sadece basariyla islendikten sonra kuyruktan silinir
            // (production-ready pratik - autoAck ile mesaj islenmeden kaybolabilir).
            _channel!.BasicAck(ea.DeliveryTag, multiple: false);
        };

        _channel.BasicConsume(queue: RabbitMqPublisherService.QueueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("RabbitMQ consumer baslatildi. Queue: {Queue}", RabbitMqPublisherService.QueueName);

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        base.Dispose();
    }
}
