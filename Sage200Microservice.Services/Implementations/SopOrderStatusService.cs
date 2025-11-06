using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Shared;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.Services.Implementations.Sop
{
    /// <summary>
    /// Implementation of SOP Orders Status amendment using Sage endpoint "sop_orders_status".
    /// The Sage 200 2025 R1 release introduced this endpoint to amend order status via API.
    /// </summary>
    public sealed class SopOrderStatusService : ISopOrderStatusService
    {
        private readonly ISageApiClient _api;
        private readonly ILogger<SopOrderStatusService> _log;

        public SopOrderStatusService(ISageApiClient api, ILogger<SopOrderStatusService> log)
        {
            _api = api;
            _log = log;
        }

        /// <inheritdoc />
        public async Task<SopOrderStatusUpdateResult> UpdateStatusAsync(SopOrderStatusUpdate request, HttpContext http, CancellationToken ct)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.OrderId <= 0) throw new ArgumentOutOfRangeException(nameof(request.OrderId), "OrderId must be > 0.");
            if (string.IsNullOrWhiteSpace(request.Status)) throw new ArgumentException("Status is required.", nameof(request.Status));

            var enumLiteral = StatusMapping.ToSageEnum(request.Status);
            var payload = new Dictionary<string, object?>
            {
                // The new endpoint is exposed as a separate resource in 2025 R1:
                // POST /sop_orders_status
                // Payload fields are kept minimal here and pass-through to Sage:
                ["sop_order_id"] = request.OrderId,
                ["document_status"] = enumLiteral,
            };

            if (!string.IsNullOrWhiteSpace(request.Reason))
            {
                // Some builds include a free-text reason/notes field on the status change model.
                payload["reason"] = request.Reason;
            }

            _log.LogInformation("Posting status change to Sage: sop_order_id={OrderId}, document_status={Status}", request.OrderId, enumLiteral);

            // Execute
            var json = await _api.PostJsonAsync<Dictionary<string, object?>, JsonDocument>(
                "sop_orders_status", payload, ct);

            // Try materialise new status from response (defensive)
            string? returned = null;
            try
            {
                if (json.RootElement.TryGetProperty("document_status", out var ds))
                {
                    returned = ds.GetString();
                }
                else if (json.RootElement.TryGetProperty("status", out var s))
                {
                    returned = s.GetString();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to parse returned document_status.");
            }

            var friendly = StatusMapping.NormalizeFromSage(returned ?? enumLiteral);
            return new SopOrderStatusUpdateResult
            {
                Success = true,
                Message = "Status updated.",
                OrderId = request.OrderId,
                NewStatus = friendly
            };
        }
    }
}
