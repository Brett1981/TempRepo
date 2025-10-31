namespace Sage200Microservice.Data.Models;

/// <summary>
/// Central audit log for all operations (API, Kafka, DLQ, etc.)
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    
    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Category: Business, System, Security
    /// </summary>
    public string Category { get; set; } = "System";
    
    /// <summary>
    /// Event type
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// Severity: Info, Warning, Error
    /// </summary>
    public string Severity { get; set; } = "Info";
    
    /// <summary>
    /// Action performed (e.g., ResultReceived, PaymentSync, ApiValidation)
    /// </summary>
    public string Action { get; set; } = string.Empty;
    
    /// <summary>
    /// Status: Success, Failure, InProgress
    /// </summary>
    public string Status { get; set; } = "Success";
    
    /// <summary>
    /// Correlation ID for tracing
    /// </summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// API key ID or application name
    /// </summary>
    public int? ApiKeyId { get; set; }
    
    /// <summary>
    /// User ID
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// Client identifier
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Resource being accessed
    /// </summary>
    public string? Resource { get; set; }
    
    /// <summary>
    /// External reference
    /// </summary>
    public string? ExternalRef { get; set; }
    
    /// <summary>
    /// Detailed description of event
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Duration in milliseconds (optional)
    /// </summary>
    public int? DurationMs { get; set; }
    
    /// <summary>
    /// Source IP address
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// HTTP method
    /// </summary>
    public string? HttpMethod { get; set; }
    
    /// <summary>
    /// URL path
    /// </summary>
    public string? UrlPath { get; set; }
    
    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Expiration timestamp
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    // Navigation property
    public ApiKey? ApiKey { get; set; }
}
