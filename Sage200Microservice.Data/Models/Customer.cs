namespace Sage200Microservice.Data.Models;

/// <summary>
/// Represents a customer entity
/// </summary>
public class Customer
{
    public int Id { get; set; }
    
    /// <summary>
    /// Customer name
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique customer code (Sage account code)
    /// </summary>
    public string CustomerCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Address line 1
    /// </summary>
    public string? AddressLine1 { get; set; }
    
    /// <summary>
    /// Address line 2
    /// </summary>
    public string? AddressLine2 { get; set; }
    
    /// <summary>
    /// City
    /// </summary>
    public string? City { get; set; }
    
    /// <summary>
    /// Postcode
    /// </summary>
    public string? Postcode { get; set; }
    
    /// <summary>
    /// Telephone number
    /// </summary>
    public string? Telephone { get; set; }
    
    /// <summary>
    /// Email address
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// When this was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who created this
    /// </summary>
    public string CreatedBy { get; set; } = "System";
    
    /// <summary>
    /// Sage ID
    /// </summary>
    public string? SageId { get; set; }
    
    /// <summary>
    /// Last sync timestamp
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}
