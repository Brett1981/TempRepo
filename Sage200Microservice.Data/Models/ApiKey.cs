namespace Sage200Microservice.Data.Models;

/// <summary>
/// Represents an API key for a calling application
/// </summary>
public class ApiKey
{
    public int Id { get; set; }
    
    /// <summary>
    /// The API key string (GUID format)
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the calling application or organization
    /// </summary>
    public string ClientName { get; set; } = string.Empty;
    
    /// <summary>
    /// When this key was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether the key is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When this key expires (nullable)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// Contact email for operational alerts
    /// </summary>
    public string? ContactEmail { get; set; }
    
    /// <summary>
    /// Comma-separated permissions/scopes (future use)
    /// </summary>
    public string? Permissions { get; set; }
    
    /// <summary>
    /// Last time this key was used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
    
    /// <summary>
    /// Previous key during rotation
    /// </summary>
    public string? PreviousKey { get; set; }
    
    /// <summary>
    /// When the previous key expires
    /// </summary>
    public DateTime? PreviousKeyExpiresAt { get; set; }
    
    /// <summary>
    /// Grace period end for old key
    /// </summary>
    public DateTime? GracePeriodEnd { get; set; }
    
    /// <summary>
    /// Key version number
    /// </summary>
    public int Version { get; set; } = 1;
    
    /// <summary>
    /// Allowed IP addresses (JSON or CSV)
    /// </summary>
    public string? AllowedIpAddresses { get; set; }
}
