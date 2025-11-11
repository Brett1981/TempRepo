using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.API.Controllers.Infrastructure; // SageRouteControllerBase
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sales;
using Sage200Microservice.Services.Shared;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class SalesInvoicesController : SageRouteControllerBase
    {
        private readonly ISalesInvoicesService _svc;
        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;
        private readonly IIdempotencyRecordRepository _idem;
        private readonly IAuditLogService _audit;
        private readonly SageApiSettings _sageApiSettings;
        private readonly ILogger<SalesInvoicesController> _logger;

        public SalesInvoicesController(
            ISalesInvoicesService svc,
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys,
            IIdempotencyRecordRepository idem,
            IAuditLogService audit,
            IOptions<SageApiSettings> sageApiOptions,
            ISageApiClient sage,                                 // for SageRouteControllerBase
            ILogger<SalesInvoicesController> logger)
            : base(sage, logger)
        {
            _svc = svc;
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
            _idem = idem;
            _audit = audit;
            _sageApiSettings = sageApiOptions.Value;
            _logger = logger;
        }

        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(SalesCreateResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        // You can keep this attribute; base-class discovery also protects you.
        [SageRoutingHeaders(RequiresIdempotencyKey = true)]
        public async Task<IActionResult> CreateAsync([FromBody] SalesInvoiceCreate body, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            // Ensure routing headers exist (from headers or discovery via /sites)
            await EnsureRoutingAsync(ct);

            // Stage 4: Context (now guaranteed by EnsureRoutingAsync)
            string siteId = Request.Headers["X-Site"].ToString();
            string companyId = Request.Headers["X-Company"].ToString();
            string? idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out StringValues idemHeader)
                ? (string.IsNullOrWhiteSpace(idemHeader) ? null : idemHeader.ToString())
                : null;

            // Fallback to deterministic key if caller didn’t supply one
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = GenerateStableIdempotencyKey(body);
                Request.Headers["Idempotency-Key"] = idempotencyKey;
            }

            string correlationId = HttpContext.TraceIdentifier;

            var requestContext = new RequestContext(siteId, companyId, idempotencyKey, correlationId);
            _logger.LogDebug("SalesInvoice Create context: Site={Site} Company={Comp} HasIdem={HasIdem}",
                siteId, companyId, idempotencyKey is not null);

            // Resolve AppId when externalRefs present but appId omitted
            int? headerAppId = null;
            if (body.ExternalRefs is { Count: > 0 } && body.ExternalRefs.Any(x => x.AppId is null))
            {
                var apiKey = Request.Headers["X-Api-Key"].ToString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
                    var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
                    if (keyRow == null || !valid)
                        return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                        {
                            Type = "https://httpstatuses.com/401",
                            Title = "Unauthorized",
                            Status = StatusCodes.Status401Unauthorized,
                            Detail = "API key could not be resolved to a valid AppId."
                        });

                    await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
                    headerAppId = keyRow.Id;
                }
            }

            // Call service with explicit RequestContext
            var result = await _svc.CreateAsync(body, requestContext, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Urn))
            {
                if (result.Failure == FailureKind.Upstream)
                {
                    // audit – upstream failure
                    TryAudit(() => _audit.LogDataModificationEventAsync(
                        userId: User?.Identity?.Name,
                        clientId: Request.Headers["X-Api-Key"].ToString(),
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0",
                        resource: "SalesInvoice",
                        referenceId: result.UpstreamStatusCode?.ToString(),
                        referenceName: "Sage200",
                        action: "Create",
                        status: (result.UpstreamStatusCode is >= 400 and < 500) ? AuditEventStatus.Failure : AuditEventStatus.Denied,
                        description: "Sales invoice create failed (upstream).",
                        previousState: null,
                        newState: null,
                        details: JsonSerializer.Serialize(new
                        {
                            upstreamStatus = result.UpstreamStatusCode,
                            upstreamPreview = Sage200Microservice.Services.Shared.Helpers.Truncate(result.UpstreamBody, 512),
                            headers = new { siteId, companyId, idempotencyKey },
                            body
                        }),
                        correlationId: correlationId), ct);

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

                // validation/model failure audit
                TryAudit(() => _audit.LogDataModificationEventAsync(
                    userId: User?.Identity?.Name,
                    clientId: Request.Headers["X-Api-Key"].ToString(),
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0",
                    resource: "SalesInvoice",
                    referenceId: "validation",
                    referenceName: "ModelState",
                    action: "Create",
                    status: AuditEventStatus.Failure,
                    description: "Sales invoice validation failed.",
                    previousState: null,
                    newState: null,
                    details: JsonSerializer.Serialize(new { modelState = ModelState, headers = new { siteId, companyId, idempotencyKey }, body }),
                    correlationId: correlationId), ct);

                return ValidationProblem(ModelState);
            }

            var sageUrn = result.Urn!;

            // success audit
            TryAudit(() => _audit.LogDataModificationEventAsync(
                userId: User?.Identity?.Name,
                clientId: Request.Headers["X-Api-Key"].ToString(),
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0",
                resource: "SalesInvoice",
                referenceId: sageUrn,
                referenceName: "SageURN",
                action: "Create",
                status: AuditEventStatus.Success,
                description: "Sales invoice created successfully.",
                previousState: null,
                newState: null,
                details: JsonSerializer.Serialize(new
                {
                    urn = sageUrn,
                    customer_id = body.CustomerId,
                    totals = new
                    {
                        goods = body.DocumentGoodsValue,
                        tax = body.DocumentTaxValue,
                        discount = body.DocumentDiscountValue,
                        tax_discount = body.DocumentTaxDiscountValue
                    },
                    headers = new { siteId, companyId, idempotencyKey },
                    externalRefs = body.ExternalRefs
                }),
                correlationId: correlationId), ct);

            // Persist mappings + idempotency outcome
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
                                EntityType = ExternalEntityType.SalesInvoice,
                                SageId = null,
                                SageUrn = sageUrn,
                                ExternalRef = item.ExternalRef
                            }, ct);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        await _idem.UpsertResultUrnAsync(idempotencyKey, resource: "sales_invoices", resultSageUrn: sageUrn, expiresUtc: null, ct);
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

        // ---------- helpers ----------

        private static string GenerateStableIdempotencyKey(SalesInvoiceCreate body)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash);
        }

        private void TryAudit(Func<Task> write, CancellationToken ct)
        {
            try { _ = write(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Audit log write failed (ignored)."); }
        }
    }
}
