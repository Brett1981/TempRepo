// ================================================================
// File: Services/Models/Sop/SopOrderDtos.cs
// Project: Sage200ApiMicroservice (Services)
// Purpose: DTOs and query model for SOP Orders
// Notes:
//  - ID-first for identifiers.
//  - SourceExternalId fields map to Sage spare text fields (see mapping notes).
// ================================================================
namespace Sage200Microservice.Services.Models.Sop
{
    /// <summary>
    /// Query parameters for listing SOP orders via Sage OData.
    /// </summary>
    public sealed class SopOrderQuery
    {
        /// <summary>Raw OData $filter to pass through.</summary>
        public string? ODataFilter { get; set; }

        /// <summary>OData $orderby.</summary>
        public string? OrderBy { get; set; }

        /// <summary>OData $top.</summary>
        public int? Top { get; set; }

        /// <summary>OData $skip.</summary>
        public int? Skip { get; set; }

        /// <summary>Friendly: filter by Sage customer id.</summary>
        public long? CustomerId { get; set; }

        /// <summary>Friendly: filter by order number/reference.</summary>
        public string? OrderNo { get; set; }

        /// <summary>Friendly: filter by status (either friendly or code string).</summary>
        public string? Status { get; set; }

        /// <summary>Friendly: filter from order date (inclusive, UTC).</summary>
        public DateTime? FromDate { get; set; }

        /// <summary>Friendly: filter to order date (inclusive, UTC).</summary>
        public DateTime? ToDate { get; set; }

        /// <summary>
        /// If set to false, the service will omit $count=true when building the OData URL.
        /// If null (default), the service uses its default behavior (include $count).
        /// </summary>
        public bool? IncludeCount { get; set; }

        /// <summary>
        /// Optional numeric whitelist for document_status (e.g., {0,1,3,5,6}).
        /// When provided, the service will OR these codes into the filter and MAY use
        /// a multi-call fallback if the single filtered call fails (e.g., 502).
        /// </summary>
        public IReadOnlyList<int>? StatusWhitelist { get; set; }
    }

    public sealed class SopOrderDto
    {
        public long Id { get; set; }
        public string? OrderNo { get; set; }        // maps from number/reference depending on tenant
        public long CustomerId { get; set; }
        public string? CustomerReference { get; set; }
        public string? Status { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? PromisedDate { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal? NetTotal { get; set; }
        public decimal? TaxTotal { get; set; }
        public decimal? GrossTotal { get; set; }

        // External reconciliation
        public string? SourceExternalId { get; set; }

        public List<SopOrderLineDto> Lines { get; set; } = new();
    }

    public sealed class SopOrderLineDto
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public int LineNumber { get; set; }
        public string? ProductCode { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal NetTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal GrossTotal { get; set; }

        // External reconciliation
        public string? SourceExternalLineId { get; set; }
    }
}
