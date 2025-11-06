using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service for amending SOP Order document status using Sage 200 Professional 2025 R1
    /// "SOP-Orders-Status" endpoint (sop_orders_status).
    /// </summary>
    public interface ISopOrderStatusService
    {
        /// <summary>
        /// Updates the Sage SOP Order document status.
        /// Maps friendly status (Live/OnHold/Cancelled/Completed) to Sage enum literal
        /// (EnumDocumentStatusLive, EnumDocumentStatusOnHold, EnumDocumentStatusCancelled, EnumDocumentStatusComplete).
        /// </summary>
        /// <param name="request">Order id + target status + optional reason.</param>
        /// <param name="http">Current HttpContext for correlation/tenant enrichment.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Result indicating success and the new status as returned by Sage.</returns>
        Task<SopOrderStatusUpdateResult> UpdateStatusAsync(SopOrderStatusUpdate request, HttpContext http, CancellationToken ct);
    }
}
