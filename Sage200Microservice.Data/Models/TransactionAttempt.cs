namespace Sage200Microservice.Data.Models;

/// <summary>
/// Tracks every inbound or outbound Kafka event for idempotency and replay safety
/// </summary>
public class TransactionAttempt
{
    public int Id { get; set; }
    
    /// <summary>
    /// Unique trace ID for message chain
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
    
    /// <summary>
    /// When message was received
    /// </summary>
    public DateTime ReceivedTimestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source system identifier
    /// </summary>
    public string SourceSystem { get; set; } = string.Empty;
    
    /// <summary>
    /// Triggering event identifier
    /// </summary>
    public string TriggeringEventId { get; set; } = string.Empty;
    
    /// <summary>
    /// SHA-512 hash of idempotency key
    /// </summary>
    public string? IdempotencyKeyHash { get; set; }
    
    /// <summary>
    /// Current processing status
    /// </summary>
    public string ProcessingStatus { get; set; } = "Received";
    
    /// <summary>
    /// Kafka topic name
    /// </summary>
    public string? KafkaTopic { get; set; }
    
    /// <summary>
    /// Kafka message key
    /// </summary>
    public string? KafkaMessageKey { get; set; }
    
    /// <summary>
    /// Kafka partition
    /// </summary>
    public int? KafkaPartition { get; set; }
    
    /// <summary>
    /// Kafka offset
    /// </summary>
    public long? KafkaOffset { get; set; }
    
    /// <summary>
    /// FK to ApiKeys table
    /// </summary>
    public int ApiKeyId { get; set; }
    
    /// <summary>
    /// Site ID
    /// </summary>
    public string? SiteId { get; set; }
    
    /// <summary>
    /// Company ID
    /// </summary>
    public string? CompanyId { get; set; }
    
    /// <summary>
    /// Payload data
    /// </summary>
    public byte[]? Payload { get; set; }
    
    /// <summary>
    /// Sage URN returned by API
    /// </summary>
    public string? SageUrn { get; set; }
    
    /// <summary>
    /// Sage ID returned by API
    /// </summary>
    public long? SageId { get; set; }
    
    /// <summary>
    /// When processing started
    /// </summary>
    public DateTime? ProcessingStartedUtc { get; set; }
    
    /// <summary>
    /// When processing completed
    /// </summary>
    public DateTime? ProcessingCompletedUtc { get; set; }
    
    /// <summary>
    /// Result message or error summary
    /// </summary>
    public string? ResultMessage { get; set; }
    
    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }
    
    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// Attempt number
    /// </summary>
    public int AttemptNumber { get; set; } = 1;
    
    /// <summary>
    /// Result code
    /// </summary>
    public string? ResultCode { get; set; }
    
    /// <summary>
    /// Original headers JSON
    /// </summary>
    public string? OriginalHeadersJson { get; set; }
    
    /// <summary>
    /// Entity type being processed
    /// </summary>
    public string? EntityType { get; set; }
    
    /// <summary>
    /// External reference from calling app
    /// </summary>
    public string? ExternalRef { get; set; }
    
    // Navigation property
    public ApiKey? ApiKey { get; set; }
}
