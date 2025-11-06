namespace Sage200Microservice.Data.Models;

/// <summary>
/// Stores idempotency results keyed by a hash of the Idempotency-Key header.
/// Prevents duplicate creates and enables safe retries.
/// </summary>
public class IdempotencyRecord
{
    public int Id { get; set; }

    /// <summary>Base64Url-encoded SHA256 of the idempotency key.</summary>
    public string KeyHash { get; set; } = default!;

    /// <summary>Hash of the normalized request body (optional, diagnostic).</summary>
    public string? RequestHash { get; set; }

    /// <summary>Created resource id (Sage SOP order id).</summary>
    public long? ResourceId { get; set; }

    public string? ResultSageUrn { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}
