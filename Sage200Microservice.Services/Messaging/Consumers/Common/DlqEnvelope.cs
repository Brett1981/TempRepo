// Purpose: Strongly-typed DLQ payload mirrored from the anonymous objects used by the result consumers.
// Notes:
//   • Keep this minimal and serialization-friendly.
//   • Headers are captured as a simple string dictionary for portability.
//   • Aligns with: { correlationId, reason, originalPayload, headers, occurredUtc }.
// =====================================================================================================
namespace Sage200Microservice.Services.Messaging.Consumers.Common
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Canonical Dead Letter Queue envelope published when a result message cannot be processed
    /// (e.g., no matching TransactionAttempt, invalid payload).
    /// </summary>
    public sealed class DlqEnvelope
    {
        /// <summary>
        /// Correlation identifier (often the Kafka message key). May be null if unavailable.
        /// </summary>
        [JsonPropertyName("correlationId")]
        public string? CorrelationId { get; init; }

        /// <summary>
        /// Human-readable reason for dead-lettering (kept concise; avoid sensitive data).
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// The original message payload (raw), trimmed upstream if extremely large.
        /// </summary>
        [JsonPropertyName("originalPayload")]
        public string OriginalPayload { get; init; } = string.Empty;

        /// <summary>
        /// Message headers flattened to a string dictionary for portability.
        /// </summary>
        [JsonPropertyName("headers")]
        public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// UTC timestamp when the message was routed to the DLQ.
        /// </summary>
        [JsonPropertyName("occurredUtc")]
        public DateTime OccurredUtc { get; init; } = DateTime.UtcNow;
    }
}