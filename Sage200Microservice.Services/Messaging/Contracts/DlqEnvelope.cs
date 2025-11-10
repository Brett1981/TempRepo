using System;
using System.Text.Json;


namespace Sage200Microservice.Services.Messaging.Contracts
{
    /// <summary>
    /// Canonical DLQ envelope used when a message has exceeded retry policy or hit a permanent error.
    /// </summary>
    public sealed class DlqEnvelope
    {
        public required string CorrelationId { get; init; }
        public required string OriginalTopic { get; init; }
        public string? EntityType { get; init; }
        public string? ExternalReference { get; init; }
        public required string ErrorCategory { get; init; } // Transient | Permanent
        public required string ErrorMessage { get; init; }
        public string? StackTrace { get; init; }
        public required string OriginalPayload { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;


        public string ToJson() => JsonSerializer.Serialize(this);


        public static DlqEnvelope FromJson(string json) => JsonSerializer.Deserialize<DlqEnvelope>(json)!;
    }
}