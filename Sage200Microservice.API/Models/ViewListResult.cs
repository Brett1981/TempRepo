// =========================================================================================================
// API/Models/ViewListResult.cs
// - Lightweight envelope for paged “view” reads (e.g., Sage /views endpoints).
// - Used by CustomerSopInvoiceCreditLineViewController (and reusable for other view endpoints).
// .NET 9+
// =========================================================================================================

using System.Collections.Generic;

namespace Sage200Microservice.API.Models
{
    /// <summary>
    /// Normalized list result for view queries with optional continuation.
    /// </summary>
    /// <typeparam name="T">The DTO type for each item in the page.</typeparam>
    public sealed class ViewListResult<T>
    {
        /// <summary>
        /// Returned items for the current page.
        /// </summary>
        public List<T> Items { get; set; } = new();

        /// <summary>
        /// Provider continuation token or URL (e.g., "next", "next_page", "@odata.nextLink").
        /// If null/empty, there is no known next page.
        /// </summary>
        public string? Next { get; set; }

        /// <summary>
        /// Raw upstream payload (populated only when parsing fails, for diagnostics).
        /// </summary>
        public string? Raw { get; set; }
    }
}
