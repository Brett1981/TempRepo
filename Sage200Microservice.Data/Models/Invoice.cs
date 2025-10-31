namespace Sage200Microservice.Data.Models;

/// <summary>
/// Represents an invoice entity
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    
    /// <summary>
    /// Invoice reference number
    /// </summary>
    public string InvoiceReference { get; set; } = string.Empty;
    
    /// <summary>
    /// FK to Customer
    /// </summary>
    public int CustomerId { get; set; }
    
    /// <summary>
    /// Gross invoice value
    /// </summary>
    public decimal GrossValue { get; set; }
    
    /// <summary>
    /// Outstanding amount
    /// </summary>
    public decimal OutstandingValue { get; set; }
    
    /// <summary>
    /// Invoice status
    /// </summary>
    public string Status { get; set; } = "Pending";
    
    /// <summary>
    /// When this was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last checked timestamp
    /// </summary>
    public DateTime? LastCheckedAt { get; set; }
    
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
    
    // Navigation property
    public Customer? Customer { get; set; }
}
