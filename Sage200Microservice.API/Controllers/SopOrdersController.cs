using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sales;
using Sage200Microservice.Services.Models.Sop;
using Sage200Microservice.Services.Shared;

namespace Sage200Microservice.API.Controllers;

/// <summary>
/// REST endpoints for SOP Orders. ID-first contracts.
/// </summary>
[ApiController]
[Route("api/sop/orders")]
[Produces("application/json")]
public sealed class SopOrdersController : ControllerBase
{
    /// <summary>
    /// Whitelist of "outstanding" document_status codes for your tenant:
    /// Live(0), On hold(1), Disputed(3), Draft(5), Printed(6).
    /// </summary>
    private static readonly int[] OutstandingWhitelist = { 0, 1, 3, 5, 6 };

    private readonly ISopOrderService _svc;
    private readonly ILogger<SopOrdersController> _log;
    private readonly IConfiguration _cfg;

    private readonly ApplicationContext _db;
    private readonly IExternalIdLinkRepository _links;
    private readonly IApiKeyRepository _apiKeys;

    public SopOrdersController(
        ISopOrderService svc,
        ILogger<SopOrdersController> log,
        IConfiguration cfg,
        ApplicationContext db,
        IExternalIdLinkRepository links,
        IApiKeyRepository apiKeys)
    {
        _svc = svc;
        _log = log;
        _cfg = cfg;

        _db = db;
        _links = links;
        _apiKeys = apiKeys;
    }

    /// <summary>Lists SOP orders (header-only). Honors feature flag Features:Sop:OrdersListEnabled.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SopOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] SopOrderQuery query, CancellationToken ct)
    {
        if (!_cfg.GetValue("Features:Sop:OrdersListEnabled", true)) return NotFound();
        var page = await _svc.ListAsync(query, HttpContext, ct);
        return Ok(page);
    }

    /// <summary>Gets SOP order by id including lines. Honors feature flag Features:Sop:OrdersGetEnabled.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(SopOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute] long id, CancellationToken ct)
    {
        if (!_cfg.GetValue("Features:Sop:OrdersGetEnabled", true)) return NotFound();
        var dto = await _svc.GetAsync(id, HttpContext, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Creates a SOP order (ID-first).
    /// Reads Idempotency-Key from header if body.IdempotencyKey is not provided.
    /// Honors feature flag Features:Sop:OrdersCreateEnabled.
    /// Accepts optional ExternalRefs on the request to create cross-app → Sage mappings.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SopOrderCreateResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    // [SageRoutingHeaders(RequiresIdempotencyKey = true)]
    public async Task<IActionResult> CreateAsync([FromBody] SopOrderCreate body, CancellationToken ct)
    {
        if (!_cfg.GetValue("Features:Sop:OrdersCreateEnabled", true)) return NotFound();

        // Fallback Idempotency-Key from header
        body.IdempotencyKey ??= Request.Headers["Idempotency-Key"].ToString();

        // Resolve AppId if external refs are supplied and no explicit appId exists on items
        int? headerAppId = null;
        if (body.ExternalRefs != null && body.ExternalRefs.Count > 0)
        {
            var apiKey = Request.Headers["X-Api-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct)
                             ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
                var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
                if (keyRow == null || !valid)
                {
                    return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/401",
                        Title = "Unauthorized",
                        Status = StatusCodes.Status401Unauthorized,
                        Detail = "API key could not be resolved to a valid AppId."
                    });
                }
                await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
                headerAppId = keyRow.Id;
            }
        }

        // 1) Call Sage (NO DB transaction around outbound HTTP)
        var result = await _svc.CreateAsync(body, HttpContext, ct);
        if (!result.Success)
        {
            if (result.Failure == FailureKind.Upstream)
            {
                var pd = new ProblemDetails
                {
                    Type = "https://httpstatuses.com/502",
                    Title = "Upstream Sage error",
                    Status = StatusCodes.Status502BadGateway,
                    Detail = string.IsNullOrWhiteSpace(result.UpstreamBody)
                        ? (result.Message ?? "Upstream error from Sage.")
                        : result.UpstreamBody
                };
                return StatusCode(StatusCodes.Status502BadGateway, pd);
            }
            return BadRequest(result);
        }

        // 2) DB-only persistence AFTER success (ExecutionStrategy, no HTTP inside)
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await _links.TryInsertAsync(new ExternalIdLink
            {
                SageUrn = result.OrderId.ToString(), // if you move to real URN later, swap here
                SageId = result.OrderId,
                EntityType = ExternalEntityType.SopOrder,
                CreatedUtc = DateTime.UtcNow,
                ExternalRef = body.Header?.SourceExternalId
                              ?? body.ExternalRefs?.FirstOrDefault()?.ExternalRef
                              ?? body.Header?.CustomerReference
                              ?? string.Empty,
                AppId = body.ExternalRefs?.FirstOrDefault()?.AppId
                              ?? headerAppId
                              ?? 5
            }, ct);
        });

        // 201 Created, with location header to GET by id
        return CreatedAtAction(nameof(GetAsync), new { id = result.OrderId }, result);
    }

    /// <summary>
    /// Returns SOP orders that are "Outstanding" by WHITELIST:
    /// Live(0), On hold(1), Disputed(3), Draft(5), Printed(6).
    /// Lists outstanding SOP orders. Supports OData paging/sort and friendly filters.
    /// </summary>
    [HttpGet("outstanding")]
    [ProducesResponseType(typeof(PagedResult<SopOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutstandingAsync(
        [FromQuery(Name = "$filter")] string? filter,
        [FromQuery(Name = "$orderby")] string? orderBy,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery] long? customerId,
        [FromQuery] string? orderNo,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        // Inject predicate "(document_status in {0,1,3,5,6})" as an OData OR-chain.
        var injected = FriendlyFilters.AppendOutstandingWhitelist(filter);

        var query = new SopOrderQuery
        {
            ODataFilter = injected,
            OrderBy = orderBy ?? "order_date desc",
            Top = top ?? 10,
            Skip = skip ?? 0,
            CustomerId = customerId,
            OrderNo = orderNo,
            FromDate = fromDate,
            ToDate = toDate,
            IncludeCount = false,
            StatusWhitelist = OutstandingWhitelist
        };

        var page = await _svc.ListAsync(query, HttpContext, ct);
        return Ok(page);
    }
}
