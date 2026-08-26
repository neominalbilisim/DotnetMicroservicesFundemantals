using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Uygulama açılışında "demo-events" topic'inin doğru ayarlarla (5 partition,
/// replication factor=1 — tek broker'lı KRaft cluster'ımıza uygun) var olmasını
/// garanti eder. Topic zaten varsa hiçbir şey yapmaz (idempotent).
///
/// Bu, Kafka'nın "auto.create.topics.enable" ayarına bağımlı kalmadan,
/// uygulamanın kendi topic'ini kendisinin sahiplenmesini sağlayan
/// production-ready bir pratiktir.
/// </summary>
public static class KafkaTopicInitializer
{
    private const int PartitionCount = 5;
    private const short ReplicationFactor = 1;
    private const int MaxAttempts = 5;
    private const int RetryDelayMs = 3000;

    public static async Task EnsureTopicsExistAsync(IConfiguration configuration, ILogger logger)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9094";

        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification
                    {
                        Name = KafkaConsumerHostedService.Topic,
                        NumPartitions = PartitionCount,
                        ReplicationFactor = ReplicationFactor
                    }
                });

                logger.LogInformation(
                    "Kafka topic '{Topic}' basariyla olusturuldu ({Partitions} partition, replication factor={ReplicationFactor}).",
                    KafkaConsumerHostedService.Topic, PartitionCount, ReplicationFactor);
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                logger.LogInformation("Kafka topic '{Topic}' zaten mevcut, olusturma adimi atlandi.", KafkaConsumerHostedService.Topic);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex,
                    "Kafka topic '{Topic}' olusturulamadi (deneme {Attempt}/{MaxAttempts}). Kafka henuz hazir olmayabilir, {DelayMs} ms sonra tekrar denenecek.",
                    KafkaConsumerHostedService.Topic, attempt, MaxAttempts, RetryDelayMs);
                await Task.Delay(RetryDelayMs);
            }
        }

        logger.LogError(
            "Kafka topic '{Topic}' {MaxAttempts} denemeden sonra olusturulamadi. Kafka'nin ayakta ve erisilebilir oldugunu kontrol edin.",
            KafkaConsumerHostedService.Topic, MaxAttempts);
    }
}
