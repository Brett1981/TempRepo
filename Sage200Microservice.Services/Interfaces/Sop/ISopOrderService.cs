using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.Services.Interfaces;

/// <summary>
/// SOP Orders service contract (REST + gRPC façade).
/// </summary>
public interface ISopOrderService
{
    /// <summary>Lists SOP orders with OData passthrough and friendly filters.</summary>
    Task<PagedResult<SopOrderDto>> ListAsync(SopOrderQuery query, HttpContext http, CancellationToken ct);

    /// <summary>Gets an order with lines.</summary>
    Task<SopOrderDto?> GetAsync(long id, HttpContext http, CancellationToken ct);

    /// <summary>Create SOP order with idempotency and full logging.</summary>
    Task<SopOrderCreateResult> CreateAsync(SopOrderCreate request, HttpContext http, CancellationToken ct);

    Task<SopOrderCreateResult> CreateSopOrderAsync(SopOrderCreate request, HttpContext http, CancellationToken ct = default);
    Task<SopOrderCreateResult> CreateSopOrderAsync(SopOrderPayload sopOrder, string customerUrn, RequestContext context, CancellationToken ct);
}
