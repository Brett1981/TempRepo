using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Implementation of IKafkaConsumer using the Confluent.Kafka library.
    /// </summary>
    public sealed class ConfluentKafkaConsumer : IKafkaConsumer
    {
        private readonly ILogger<ConfluentKafkaConsumer> _logger;
        private readonly IConsumer<Ignore, string> _consumer;
        private readonly KafkaOptions _options;

        public ConfluentKafkaConsumer(IOptions<KafkaOptions> options, ILogger<ConfluentKafkaConsumer> logger)
        {
            _logger = logger;
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            var config = new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers ?? "localhost:9092",
                GroupId = _options.ConsumerGroupId ?? "sage200-microservice-default-group",
                ClientId = string.IsNullOrWhiteSpace(_options.ClientId)
                    ? $"sage200-microservice-consumer-{Environment.MachineName}"
                    : _options.ClientId,
                AutoOffsetReset = Enum.TryParse<AutoOffsetReset>(_options.AutoOffsetReset, true, out var offsetReset)
                    ? offsetReset
                    : AutoOffsetReset.Earliest,
                EnableAutoCommit = _options.EnableAutoCommit, // Use setting from options
                MaxPollIntervalMs = _options.MaxPollIntervalMs ?? 300000,
                // Add SecurityProtocol, SaslMechanism etc. mirroring ProducerConfig logic if needed
            };

            if (!string.IsNullOrWhiteSpace(_options.SecurityProtocol) &&
                Enum.TryParse<SecurityProtocol>(_options.SecurityProtocol, true, out var sec))
                config.SecurityProtocol = sec;

            if (!string.IsNullOrWhiteSpace(_options.SaslMechanism) &&
                Enum.TryParse<SaslMechanism>(_options.SaslMechanism, true, out var mech))
                config.SaslMechanism = mech;

            if (!string.IsNullOrWhiteSpace(_options.SaslUsername))
                config.SaslUsername = _options.SaslUsername;
            if (!string.IsNullOrWhiteSpace(_options.SaslPassword))
                config.SaslPassword = _options.SaslPassword;


            _consumer = new ConsumerBuilder<Ignore, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka Consumer Error: {Reason} (Code: {Code}, IsFatal: {IsFatal})", e.Reason, e.Code, e.IsFatal))
                .SetStatisticsHandler((_, json) => _logger.LogDebug("Kafka Consumer Stats: {Stats}", json)) // Optional: Log stats if needed
                .Build();

            _logger.LogInformation("Kafka Consumer built with GroupId: {GroupId}", config.GroupId);
        }

        public Task SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken)
        {
            var topicList = topics.ToList();
            _logger.LogInformation("Subscribing Kafka Consumer to topics: {Topics}", string.Join(", ", topicList));
            _consumer.Subscribe(topicList);
            return Task.CompletedTask; // Subscribe is synchronous in the client library
        }

        public async Task<ConsumeResult<Ignore, string>?> ConsumeAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Consume blocks until a message arrives, timeout occurs, or cancellation token fires.
                // Using Task.Run to avoid blocking the caller thread if Consume waits for a long time.
                return await Task.Run(() => _consumer.Consume(cancellationToken), cancellationToken);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka Consume error: {Reason}", ex.Error.Reason);
                // Depending on the error code, you might want specific handling (e.g., retries for timeouts)
                // For now, we just log and return null.
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka Consume operation cancelled.");
                return null; // Graceful shutdown or cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected Kafka Consume exception.");
                return null; // Or rethrow depending on desired behavior
            }
        }

        public Task CommitAsync(ConsumeResult<Ignore, string> result)
        {
            if (_options.EnableAutoCommit)
            {
                _logger.LogWarning("Attempted to manually commit offset while EnableAutoCommit is true. Ignoring.");
                return Task.CompletedTask;
            }
            try
            {
                _consumer.Commit(result);
                _logger.LogDebug("Kafka offset committed: {TopicPartitionOffset}", result.TopicPartitionOffset);
            }
            catch (KafkaException ex)
            {
                _logger.LogError(ex, "Kafka Commit failed: {Reason}", ex.Error.Reason);
                // Decide how to handle commit failures (e.g., retry, log & move on)
                // For now, just logging.
            }
            return Task.CompletedTask;
        }

        public void Close()
        {
            _logger.LogInformation("Closing Kafka consumer.");
            _consumer.Close(); // Releases resources and leaves the consumer group
        }

        public void Dispose()
        {
            _consumer?.Dispose();
        }
    }
}