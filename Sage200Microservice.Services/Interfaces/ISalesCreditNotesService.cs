/* =========================================================================================================
 * ISalesCreditNotesService.cs  —  UPDATED CONTRACT
 * Mirrors Sales_Invoices contract shape. If we later add "defaults" (POST /sales_credit_notes_new),
 * add the GetDefaultsAsync(...) to this interface in the same pattern used for invoices.
 * ========================================================================================================= */

using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.Services.Interfaces
{
    public interface ISalesCreditNotesService
    {
        /// <summary>
        /// Create a Sales Credit Note (returns URN on success).
        /// </summary>
        Task<SalesCreateResult> CreateAsync(SalesCreditNoteCreate request, HttpContext http, CancellationToken ct);
    }
}
