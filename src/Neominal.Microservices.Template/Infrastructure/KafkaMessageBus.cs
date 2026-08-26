using System.Collections.Concurrent;
using Confluent.Kafka;

namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Senaryo: Kafka'ya event publish etme ve arka planda tüketme.
/// Not: Bu modül sadece Kafka BAĞLANTISINI doğrulamayı hedefler.
/// Outbox / Saga / MassTransit entegrasyonu bir sonraki modülde ele alınır.
/// </summary>
public interface IKafkaProducerService
{
    Task PublishAsync(string topic, string message);
}

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<Null, string> _producer;

    public KafkaProducerService(IConfiguration configuration)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9094",
            // Varsayılan 300.000 ms (5 dakika) yerine, sorunları hızlı görebilmek için
            // makul bir mesaj/istek timeout'u tanımlıyoruz.
            MessageTimeoutMs = 10000,
            RequestTimeoutMs = 10000
        };
        _producer = new ProducerBuilder<Null, string>(config).Build();
    }

    public async Task PublishAsync(string topic, string message)
    {
        await _producer.ProduceAsync(topic, new Message<Null, string> { Value = message });
    }

    public void Dispose() => _producer.Dispose();
}

/// <summary>
/// Tüketilen son N mesajı bellekte tutan basit bir depo (demo amaçlı).
/// </summary>
public class ReceivedMessagesStore
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

public class KafkaConsumerHostedService : BackgroundService
{
    public const string Topic = "demo-events";

    private readonly IConfiguration _configuration;
    private readonly ReceivedMessagesStore _store;
    private readonly ILogger<KafkaConsumerHostedService> _logger;

    public KafkaConsumerHostedService(
        IConfiguration configuration,
        ReceivedMessagesStore store,
        ILogger<KafkaConsumerHostedService> logger)
    {
        _configuration = configuration;
        _store = store;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Confluent.Kafka'nın consume döngüsü senkron/bloklayan olduğu için
        // ayrı bir thread'de (Task.Run) çalıştırılır.
        return Task.Run(() =>
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9094",
                GroupId = "demo-consumer-group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe(Topic);

            _logger.LogInformation("Kafka consumer başlatıldı. Topic: {Topic}", Topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(TimeSpan.FromSeconds(2));
                    if (result is not null)
                    {
                        _logger.LogInformation("Kafka mesajı tüketildi: {Message}", result.Message.Value);
                        _store.Add(result.Message.Value);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Kafka consume hatası");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            consumer.Close();
        }, stoppingToken);
    }
}
