namespace Sage200Microservice.Data.Models;

/// <summary>
/// Stores Sage OAuth access and refresh tokens
/// </summary>
public class OAuthToken
{
    public int Id { get; set; }
    
    /// <summary>
    /// Current valid access token
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Refresh token for long-lived authorization
    /// </summary>
    public string? RefreshToken { get; set; }
    
    /// <summary>
    /// When the access token expires
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }
    
    /// <summary>
    /// When the token was last refreshed
    /// </summary>
    public DateTime LastRefreshedUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Token type (usually "Bearer")
    /// </summary>
    public string TokenType { get; set; } = "Bearer";
    
    /// <summary>
    /// Scope of the token
    /// </summary>
    public string? Scope { get; set; }
    
    /// <summary>
    /// Site name this token is for
    /// </summary>
    public string? SiteName { get; set; }
    
    /// <summary>
    /// Company ID this token is for
    /// </summary>
    public string? CompanyId { get; set; }
}
