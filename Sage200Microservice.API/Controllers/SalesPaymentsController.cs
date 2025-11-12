using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class SalesPaymentsController : ControllerBase
    {
        private readonly ISalesPaymentsService _svc;
        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;

        public SalesPaymentsController(
            ISalesPaymentsService svc,
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys)
        {
            _svc = svc;
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
        }

        /// <summary>
        /// POST /api/SalesPayments
        /// Creates a Sales Payment (POST /sales_payments), returning the URN on success.
        /// </summary>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(SalesCreateResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        [SageRoutingHeaders(RequiresIdempotencyKey = true)]
        public async Task<IActionResult> CreateAsync([FromBody] SalesPaymentCreate body, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Validation failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more fields are invalid."
                });
            }

            // Resolve AppId from API key when ExternalRefs supplied without AppId
            int? headerAppId = null;
            if (body.ExternalRefs is { Count: > 0 } && body.ExternalRefs.Exists(x => x.AppId is null))
            {
                var apiKey = Request.Headers["X-Api-Key"].ToString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var keyRow = await _apiKeys.GetByKeyAsync(apiKey) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey);
                    var valid = await _apiKeys.IsValidKeyAsync(apiKey);
                    if (keyRow == null || !valid)
                        return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                        {
                            Type = "https://httpstatuses.com/401",
                            Title = "Unauthorized",
                            Status = StatusCodes.Status401Unauthorized,
                            Detail = "API key could not be resolved to a valid AppId."
                        });
                    await _apiKeys.UpdateLastUsedAsync(apiKey);
                    headerAppId = keyRow.Id;
                }
            }

            var result = await _svc.CreateAsync(body, HttpContext, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Urn))
            {
                if (result.Failure == FailureKind.BadRequest)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/400",
                        Title = "Bad request",
                        Status = StatusCodes.Status400BadRequest,
                        Detail = result.Message ?? "Bad request."
                    });
                }

                if (result.Failure == FailureKind.Upstream)
                {
                    var pd = new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/502",
                        Title = "Upstream error",
                        Status = StatusCodes.Status502BadGateway,
                        Detail = string.IsNullOrWhiteSpace(result.UpstreamBody) ? "Upstream error" : result.UpstreamBody
                    };
                    if (result.UpstreamStatusCode.HasValue) pd.Extensions["upstreamStatus"] = result.UpstreamStatusCode;
                    return StatusCode(StatusCodes.Status502BadGateway, pd);
                }

                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
                {
                    Type = "https://httpstatuses.com/500",
                    Title = "Unexpected error",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = result.Message ?? "Unexpected error."
                });
            }

            var sageUrn = result.Urn!;

            // Persist ExternalRefs -> URN links (same pattern as invoices/credit notes)
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    if (body.ExternalRefs != null)
                    {
                        foreach (var item in body.ExternalRefs)
                        {
                            var appId = item.AppId ?? headerAppId ?? throw new InvalidOperationException("AppId missing.");
                            await _links.TryInsertAsync(new ExternalIdLink
                            {
                                AppId = appId,
                                // IMPORTANT: ensure your ExternalEntityType contains SalesPayment; if not, add it.
                                EntityType = ExternalEntityType.SalesPayment,
                                SageId = null,
                                SageUrn = sageUrn,
                                ExternalRef = item.ExternalRef
                            }, ct);
                        }
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });

            return Ok(result);
        }
    }
}
