// =========================================================================================================
// ISalesReceiptsService.cs — CONTRACT (unchanged shape; returns SalesCreateResult / FailureKind)
// =========================================================================================================

using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service contract to create Sales Receipts in Sage 200.
    /// </summary>
    public interface ISalesReceiptsService
    {
        /// <summary>
        /// Creates a Sales Receipt (POST /sales_receipts) and returns the URN on success.
        /// Uses omit-nulls JSON and forwards X-Site/X-Company headers.
        /// </summary>
        Task<SalesCreateResult> CreateAsync(SalesReceiptCreate request, HttpContext http, CancellationToken ct);
    }

}
