using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sales;
using Sage200Microservice.Services.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class SalesInvoicesController : ControllerBase
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
            ILogger<SalesInvoicesController> logger)
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
        [SageRoutingHeaders(RequiresIdempotencyKey = true)]
        public async Task<IActionResult> CreateAsync([FromBody] SalesInvoiceCreate body, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            // --- Stage 4: Extract/Validate Context BEFORE calling service ---
            string? siteId = Request.Headers["X-Site"].ToString();
            string? companyId = Request.Headers["X-Company"].ToString();
            string? idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            string correlationId = HttpContext.TraceIdentifier; // Use built-in TraceIdentifier

            // Apply fallbacks from SageApiSettings if headers are missing
            if (string.IsNullOrWhiteSpace(siteId)) siteId = _sageApiSettings.SiteId;
            if (string.IsNullOrWhiteSpace(companyId)) companyId = _sageApiSettings.CompanyId;

            // Validate required context
            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(companyId))
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(siteId)) missing.Add("X-Site header or SageApi:SiteId config");
                if (string.IsNullOrWhiteSpace(companyId)) missing.Add("X-Company header or SageApi:CompanyId config");

                var detail = $"Missing required context: {string.Join(", ", missing)}.";
                _logger.LogWarning("SalesInvoice Create rejected: {Detail}. CorrelationId: {CorrelationId}", detail, correlationId); // Assuming _logger is injected

                // Consider writing an audit failure log here as well (status=ValidationFailure)

                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Missing required context headers or configuration.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = detail
                });
            }

            // Clean up potentially empty string values from headers
            if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null;

            var requestContext = new RequestContext(siteId, companyId, idempotencyKey, correlationId);
            _logger.LogDebug("RequestContext created: Site={SiteId}, Company={CompanyId}, IdemKeyPresent={IdemKeyPresent}",
                requestContext.SiteId, requestContext.CompanyId, requestContext.IdempotencyKey != null);
            // --- End Stage 4 Context Handling ---

            // Resolve AppId when externalRefs are provided and appId omitted
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

            // Call service with explicit context, NOT HttpContext
            var result = await _svc.CreateAsync(body, requestContext, ct);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Urn))
            {
                if (result.Failure == FailureKind.Upstream)
                {
                    // ---- Business audit: failure (upstream) ----
                    try
                    {
                        var corrId = HttpContext.TraceIdentifier;
                        var idemKey = Request.Headers["Idempotency-Key"].ToString();
                        var xSite = Request.Headers["X-Site"].ToString();
                        var xComp = Request.Headers["X-Company"].ToString();

                        await _audit.LogDataModificationEventAsync(
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
                            details: System.Text.Json.JsonSerializer.Serialize(new
                            {
                                upstreamStatus = result.UpstreamStatusCode,
                                upstreamPreview = Sage200Microservice.Services.Shared.Helpers.Truncate(result.UpstreamBody, 512),
                                headers = new
                                {
                                    xSite = Request.Headers["X-Site"].ToString(),
                                    xCompany = Request.Headers["X-Company"].ToString(),
                                    idempotencyKey = Request.Headers["Idempotency-Key"].ToString()
                                },
                                body
                            }),
                            correlationId: HttpContext.TraceIdentifier
                        );
                    }
                    catch { /* never fail the request on audit write issues */ }

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
                // ---- Business audit: failure (validation/model) ----
                try
                {
                    var corrId = HttpContext.TraceIdentifier;
                    await _audit.LogDataModificationEventAsync(
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
                        details: System.Text.Json.JsonSerializer.Serialize(new
                        {
                            modelState = ModelState,
                            headers = new
                            {
                                xSite = Request.Headers["X-Site"].ToString(),
                                xCompany = Request.Headers["X-Company"].ToString(),
                                idempotencyKey = Request.Headers["Idempotency-Key"].ToString()
                            },
                            body
                        }),
                        correlationId: HttpContext.TraceIdentifier
                    );
                }
                catch { }
                return ValidationProblem(ModelState);
            }

            var sageUrn = result.Urn!;
            // ---- Business audit: success ----
            try
            {
                var corrId = HttpContext.TraceIdentifier;
                var idemKey = Request.Headers["Idempotency-Key"].ToString();
                var xSite = Request.Headers["X-Site"].ToString();
                var xComp = Request.Headers["X-Company"].ToString();
                await _audit.LogDataModificationEventAsync(
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
                     details: System.Text.Json.JsonSerializer.Serialize(new
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
                         headers = new
                         {
                             xSite = Request.Headers["X-Site"].ToString(),
                             xCompany = Request.Headers["X-Company"].ToString(),
                             idempotencyKey = Request.Headers["Idempotency-Key"].ToString()
                         },
                         externalRefs = body.ExternalRefs
                     }),
                     correlationId: HttpContext.TraceIdentifier
                 );
            }
            catch { /* audit write must never break the success path */ }
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
                                SageUrn = result.Urn!,
                                ExternalRef = item.ExternalRef
                            }, ct);
                        }
                    }

                    // Persist idempotency outcome if caller supplied Idempotency-Key
                    var idemKey = Request.Headers["Idempotency-Key"].ToString();
                    if (!string.IsNullOrWhiteSpace(idemKey))
                    {
                        // Resource tag helps you query; align with your convention
                        await _idem.UpsertResultUrnAsync(idemKey, resource: "sales_invoices", resultSageUrn: sageUrn, expiresUtc: null, ct);
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