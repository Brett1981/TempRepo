namespace Sage200Microservice.Data.Models;

/// <summary>
/// Log of API interactions with external systems (e.g., Sage API)
/// </summary>
public class ApiLog
{
    public int Id { get; set; }
    
    /// <summary>
    /// API endpoint accessed
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP request method (GET, POST, etc.)
    /// </summary>
    public string RequestMethod { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP status code returned
    /// </summary>
    public int HttpStatusCode { get; set; }
    
    /// <summary>
    /// When the request was made
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Caller identifier
    /// </summary>
    public string? CallerId { get; set; }
    
    /// <summary>
    /// Type of API (e.g., "Sage", "External")
    /// </summary>
    public string? ApiType { get; set; }
    
    /// <summary>
    /// Request body (optional, may be truncated)
    /// </summary>
    public string? RequestBody { get; set; }
    
    /// <summary>
    /// Response body (optional, may be truncated)
    /// </summary>
    public string? ResponseBody { get; set; }
    
    /// <summary>
    /// Duration in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }
    
    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Correlation ID for tracing
    /// </summary>
    public string? CorrelationId { get; set; }
}
