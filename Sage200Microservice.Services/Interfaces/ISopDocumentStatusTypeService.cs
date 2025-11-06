using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service for retrieving SOP Document Status Types from Sage 200 (Professional 2025 R1).
    /// Corresponds to the "SOP-Document-Status-Types" API section.
    /// </summary>
    public interface ISopDocumentStatusTypeService
    {
        /// <summary>
        /// Returns the list of SOP document status types as exposed by Sage.
        /// </summary>
        /// <param name="http">HttpContext (used for correlation/tenant logging).</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<SopDocumentStatusTypeDto>> ListAsync(HttpContext http, CancellationToken ct);
    }
}
