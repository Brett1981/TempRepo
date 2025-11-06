using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.Services.Models.Sop;

/// <summary>Header model for creating a SOP order.</summary>
public sealed class SopOrderCreateHeader
{
    /// <summary>Required: Sage customer id (64-bit).</summary>
    public long CustomerId { get; set; }

    /// <summary>Optional: free text reference.</summary>
    public string? CustomerReference { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerTelephone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }

    /// <summary>Optional: promised date (UTC).</summary>
    public DateTime? PromisedDate { get; set; }

    /// <summary>Optional: ISO currency code.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>External reconciliation (stored in Sage spare_text_1).</summary>
    public string? SourceExternalId { get; set; }

    public string? Reference { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? RequestedDeliveryDate { get; set; }
    public decimal? NetValue { get; set; }
    public decimal? TaxValue { get; set; }
}

/// <summary>Create line model for SOP order.</summary>
public sealed class SopOrderCreateLine
{
    public string ProductCode { get; set; } = default!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Description { get; set; }
    public string? SourceExternalLineId { get; set; }
}

/// <summary>Aggregate create request (body for POST /api/sop/orders).</summary>
public sealed class SopOrderCreate
{
    /// <summary>
    /// Optional external references supplied by the caller for cross-app mapping.
    /// </summary>
    public List<ExternalRefItem>? ExternalRefs { get; set; }
    public SopOrderCreateHeader Header { get; set; } = new();
    public List<SopOrderCreateLine> Lines { get; set; } = new();
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Minimal external ref item for SOP create flows (kept in Services to avoid API→Services coupling).
/// </summary>
public sealed class ExternalRefItem
{
    /// <summary>Optional explicit AppId; if omitted, resolve from "X-Api-Key".</summary>
    public int? AppId { get; set; }
    /// <summary>Caller’s external reference (e.g., "BRE001").</summary>
    public string ExternalRef { get; set; } = string.Empty;
}

/// <summary>Service result for creates.</summary>
public sealed class SopOrderCreateResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? OrderId { get; set; }
    public string? OrderReference { get; set; }
    /// <summary>
    /// Typed failure (when Success == false) so controllers can map status codes.
    /// </summary>
    public FailureKind Failure { get; set; } = FailureKind.None;

    /// <summary>
    /// Optional upstream HTTP status (when Failure == Upstream).
    /// </summary>
    public int? UpstreamStatusCode { get; set; }

    /// <summary>
    /// Optional upstream body excerpt for diagnostics.
    /// </summary>
    public string? UpstreamBody { get; set; }
}

