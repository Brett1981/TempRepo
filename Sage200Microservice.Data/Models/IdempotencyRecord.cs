namespace Sage200Microservice.Data.Models;

/// <summary>
/// Maintains processed request hashes to prevent duplication
/// </summary>
public class IdempotencyRecord
{
    public int Id { get; set; }
    
    /// <summary>
    /// Hash of the idempotency key
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;
    
    /// <summary>
    /// Resource/entity type
    /// </summary>
    public string Resource { get; set; } = string.Empty;
    
    /// <summary>
    /// Hash of the entire request
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;
    
    /// <summary>
    /// FK to ApiKeys
    /// </summary>
    public int ApiKeyId { get; set; }
    
    /// <summary>
    /// When this was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Response that was returned
    /// </summary>
    public string? Response { get; set; }
    
    /// <summary>
    /// Result Sage URN
    /// </summary>
    public string? ResultSageUrn { get; set; }
    
    // Navigation property
    public ApiKey? ApiKey { get; set; }
}
