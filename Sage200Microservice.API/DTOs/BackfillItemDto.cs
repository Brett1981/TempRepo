using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// A single mapping candidate supplied to the backfill endpoint.
    /// </summary>
    public sealed class BackfillItemDto
    {
        /// <summary>
        /// Target entity type (Customer | SopOrder | SalesReceipt | SalesPayment | SalesCreditNote | SalesInvoice).
        /// </summary>
        [Required, MaxLength(40)]
        public string Entity { get; set; } = string.Empty;

        /// <summary>
        /// External reference from the calling application (e.g., "BRE001").
        /// </summary>
        [Required, MaxLength(200)]
        public string ExternalRef { get; set; } = string.Empty;

        /// <summary>
        /// Optional AppId (ApiKeys.Id). If omitted, the server will try to resolve it from 'X-Api-Key'.
        /// </summary>
        public int? AppId { get; set; }

        /// <summary>
        /// Numeric Sage Id (when canonical identifier is numeric; e.g., Customer, SopOrder).
        /// </summary>
        public long? SageId { get; set; }

        /// <summary>
        /// URN Sage Id (when canonical identifier is URN; e.g., SalesReceipt, SalesPayment, SalesCreditNote, SalesInvoice).
        /// </summary>
        [MaxLength(64)]
        public string? SageUrn { get; set; }
    }

    /// <summary>
    /// Request payload for the backfill endpoint.
    /// </summary>
    public sealed class BackfillRequestDto
    {
        /// <summary>
        /// Items to backfill (max 1,000 per call).
        /// </summary>
        [Required]
        public List<BackfillItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// Result row describing what happened (or would happen in dry-run).
    /// </summary>
    public sealed class BackfillResultItemDto
    {
        public string Entity { get; set; } = string.Empty;
        public int AppId { get; set; }
        public string ExternalRef { get; set; } = string.Empty;
        public long? SageId { get; set; }
        public string? SageUrn { get; set; }

        /// <summary>One of: inserted | exists | conflict | invalid | skipped.</summary>
        public string Outcome { get; set; } = string.Empty;

        /// <summary>Optional human-readable reason (e.g., conflict details).</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Aggregate response for backfill execution (dry or live).
    /// </summary>
    public sealed class BackfillResponseDto
    {
        public bool DryRun { get; set; }
        public int Total { get; set; }
        public int Attempted { get; set; }
        public int Inserted { get; set; }
        public int Conflicts { get; set; }
        public int Invalid { get; set; }
        public List<BackfillResultItemDto> Items { get; set; } = new();
    }
}