using Confluent.Kafka;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Strongly-typed Kafka configuration bound from configuration (Kafka: section).
    /// </summary>
    public sealed class KafkaOptions
    {
        /// <summary>Comma-separated list of bootstrap servers.</summary>
        public string? BootstrapServers { get; set; }

        /// <summary>Client id. Defaults to service name + instance if not provided.</summary>
        public string? ClientId { get; set; }

        /// <summary>Security protocol (e.g., Plaintext, SaslSsl, SaslPlaintext).</summary>
        public string? SecurityProtocol { get; set; } = nameof(Confluent.Kafka.SecurityProtocol.Plaintext);

        /// <summary>SASL mechanism (Plain, ScramSha256, ScramSha512). Optional.</summary>
        public string? SaslMechanism { get; set; }

        /// <summary>Optional SASL username.</summary>
        public string? SaslUsername { get; set; }

        /// <summary>Optional SASL password.</summary>
        public string? SaslPassword { get; set; }

        /// <summary>Acks level. Default All.</summary>
        public string? Acks { get; set; } = nameof(Confluent.Kafka.Acks.All);

        /// <summary>Enable idempotent producer (recommended for at-least-once).</summary>
        public bool EnableIdempotence { get; set; } = true;

        /// <summary>Optional default topic for testing.</summary>
        public string? DefaultTopic { get; set; }

        /// <summary>Message timeout milliseconds (optional).</summary>
        public int? MessageTimeoutMs { get; set; }

        /// <summary>Linger milliseconds (optional).</summary>
        public int? LingerMs { get; set; }

        /// <summary>Batch size bytes (optional).</summary>
        public int? BatchSize { get; set; }

        /// <summary>Compression type (optional): None, Gzip, Snappy, Lz4, Zstd.</summary>
        public string? CompressionType { get; set; }

        /// <summary>Max retry attempts on transient errors (optional).</summary>
        public int? MessageSendMaxRetries { get; set; }

        /// <summary>Retry backoff ms (optional).</summary>
        public int? RetryBackoffMs { get; set; }

        // --- Consumer Specific Options ---

        /// <summary>Consumer group ID. Required for consumers.</summary>
        public string? ConsumerGroupId { get; set; }

        /// <summary>Topic for inbound invoice create messages.</summary>
        public string? InvoiceCreateTopic { get; set; }
        // Add ClientCreateTopic, ClientSensitiveCreateTopic etc. as needed

        /// <summary>Consumer offset reset policy (Earliest, Latest). Default Earliest.</summary>
        public string? AutoOffsetReset { get; set; } = nameof(Confluent.Kafka.AutoOffsetReset.Earliest);

        /// <summary>Whether the consumer should auto-commit offsets. Default false (manual commit recommended).</summary>
        public bool EnableAutoCommit { get; set; } = false;

        /// <summary>Maximum time between poll calls before consumer is considered dead (ms). Optional.</summary>
        public int? MaxPollIntervalMs { get; set; }
    }
}
