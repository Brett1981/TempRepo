using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Services.Models.Reconciliation
{
    /// <summary>
    /// Query parameters for fetching reconciliation data (Sage Payments/Allocations linked to external references).
    /// </summary>
    public class ReconciliationQueryParameters
    {
        /// <summary>
        /// Filter by the external reference string from the calling application.
        /// Must be used in conjunction with AppId if provided.
        /// </summary>
        [StringLength(200)]
        public string? ExternalRef { get; set; }

        // AppId is passed separately to the service layer based on the authenticated API key

        /// <summary>
        /// Filter directly by the Sage URN (Unique Reference Number) for the transaction
        /// (e.g., for Sales Payments, Allocations, Invoices).
        /// </summary>
        [StringLength(128)]
        public string? SageUrn { get; set; }

        /// <summary>
        /// Optionally filter by the type of Sage entity linked.
        /// Uses the string representation of the ExternalEntityType enum.
        /// Examples: "SalesPayment", "SalesAllocation", "SalesInvoice", "Customer", "SopOrder"
        /// </summary>
        [StringLength(40)]
        public string? EntityType { get; set; }

        /// <summary>
        /// Page number for pagination (1-based).
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "PageNumber must be at least 1.")]
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Number of items per page (maximum 100).
        /// </summary>
        [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
        public int PageSize { get; set; } = 25;
    }
}