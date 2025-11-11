using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.API.Metrics;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Reconciliation;
using Sage200Microservice.Services.Models.Sales;
using Sage200Microservice.Services.Payments;
using System.Linq;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class SalesPaymentsController : ControllerBase
    {
        private readonly ILogger<SalesPaymentsController> _logger;
        private readonly ISalesPaymentsService _svc;
        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;
        private readonly ApiMetrics? _apiMetrics;
        private readonly IPaymentsAllocationService _allocations;

        public SalesPaymentsController(
            ILogger<SalesPaymentsController> logger,
            ISalesPaymentsService svc,
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys,
            ApiMetrics? apiMetrics,
            IPaymentsAllocationService allocations)
        {
            _logger = logger;
            _svc = svc;
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
            _apiMetrics = apiMetrics;
            _allocations = allocations;
        }

        /// <summary>
        /// POST /api/SalesPayments — Creates a Sales Payment (POST /sales_payments), returning the URN on success.
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
            using var _ = _apiMetrics?.TrackApiRequest("POST", "/api/SalesPayments");

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

            // Persist ExternalRefs -> URN links
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

        /// <summary>
        /// Refresh allocation values for candidate SalesInvoices and return a paged result of updates.
        /// Tenant-scoped by AppId resolved from X-Api-Key.
        /// </summary>
        [HttpGet("allocations/check")]
        [SageRoutingHeaders(DocumentApiKey = true)]
        public async Task<ActionResult<PagedResult<PaymentsAllocationCheckDto>>> CheckAllocationsAsync(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            using var _metric = _apiMetrics?.TrackApiRequest("GET", "/api/SalesPayments/allocations/check");

            // Clamp paging
            const int MaxPageSize = 200;
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            // Resolve AppId
            var apiKey = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }

            var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
            var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
            if (keyRow == null || !valid)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }
            await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
            var appId = keyRow.Id;

            // Candidate query (tracked entities; we will update them)
            var baseQuery = _db.ExternalIdLinks
                .Where(x =>
                    x.EntityType == ExternalEntityType.SalesInvoice &&
                    (x.IsFullyAllocated == false || x.IsFullyAllocated == null) &&
                    x.AppId == appId);

            var totalCount = await baseQuery.CountAsync(ct);

            var candidates = await baseQuery
                .OrderBy(x => x.LastAllocationCheckUtc == null)  // NULLs first
                .ThenBy(x => x.LastAllocationCheckUtc)
                .ThenBy(x => x.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var results = new List<PaymentsAllocationCheckDto>(candidates.Count);
            var now = DateTime.UtcNow;

            int updated = 0, failures = 0;

            foreach (var link in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var row = new PaymentsAllocationCheckDto
                {
                    AppId = link.AppId,
                    ExternalRef = link.ExternalRef,
                    SageUrn = link.SageUrn,
                    AllocatedValue = link.AllocatedValue,
                    OutstandingValue = link.OutstandingValue,
                    LastAllocationCheckUtc = link.LastAllocationCheckUtc,
                    LastAllocationChangeUtc = link.LastAllocationChangeUtc,
                    Changed = false,
                    StatusMessage = null
                };

                if (string.IsNullOrWhiteSpace(link.SageUrn))
                {
                    row.StatusMessage = "Missing Sage URN";
                    failures++;
                    results.Add(row);
                    continue;
                }

                var (alloc, outst, fully, status) = await _allocations.RefreshAllocationAsync(link.SageUrn, ct);
                if (status != null)
                {
                    row.StatusMessage = status;
                    failures++;
                }
                else
                {
                    var changed = (link.AllocatedValue != alloc) || (link.OutstandingValue != outst) || (link.IsFullyAllocated != fully);

                    link.AllocatedValue = alloc;
                    link.OutstandingValue = outst;
                    link.IsFullyAllocated = fully ?? link.IsFullyAllocated;
                    link.LastAllocationCheckUtc = now;
                    if (changed)
                    {
                        link.LastAllocationChangeUtc = now;
                        row.Changed = true;
                        updated++;
                    }

                    row.AllocatedValue = link.AllocatedValue;
                    row.OutstandingValue = link.OutstandingValue;
                    row.LastAllocationCheckUtc = link.LastAllocationCheckUtc;
                    row.LastAllocationChangeUtc = link.LastAllocationChangeUtc;
                }

                results.Add(row);
            }

            // Record a TransactionAttempt and persist changes atomically
            var correlationId =
                (Request.Headers.TryGetValue("X-Correlation-Id", out var corr) && !string.IsNullOrWhiteSpace(corr))
                    ? corr.ToString()
                    : HttpContext.TraceIdentifier;

            _db.TransactionAttempts.Add(new TransactionAttempt
            {
                SourceSystem = "API",
                TriggeringEventId = correlationId,
                EntityType = "SalesInvoice",
                ProcessingStatus = failures > 0 ? "SagePartialSuccess" : "SageSuccess",
                ApiKeyId = appId,
                ResultMessage = $"checked={results.Count}, changed={updated}, failures={failures}"
            });

            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            _logger.LogInformation(
                "AllocationsCheck: appId={AppId}, pageNumber={PageNumber}, pageSize={PageSize}, totalCount={TotalCount}, updated={Updated}, failures={Failures}",
                appId, pageNumber, pageSize, await _db.ExternalIdLinks.CountAsync(x =>
                    x.EntityType == ExternalEntityType.SalesInvoice &&
                    (x.IsFullyAllocated == false || x.IsFullyAllocated == null) &&
                    x.AppId == appId, ct), updated, failures);

            var page = new PagedResult<PaymentsAllocationCheckDto>(results, totalCount, pageNumber, pageSize);
            return Ok(page);
        }

        /// <summary>
        /// Returns a paginated list of SalesInvoice allocation candidates for the Airflow export job.
        /// </summary>
        [HttpGet("export-jobs")]
        [SageRoutingHeaders(DocumentApiKey = true)]
        public async Task<ActionResult<PagedResult<PaymentsExportJobDto>>> GetPaymentsExportJobsAsync(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            using var _ = _apiMetrics?.TrackApiRequest("GET", "/api/SalesPayments/export-jobs");

            // Clamp paging.
            const int MaxPageSize = 200;
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            // Resolve AppId
            var apiKey = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }

            var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
            var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
            if (keyRow == null || !valid)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }
            await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
            var appId = keyRow.Id;

            // Base candidate query
            var baseQuery = _db.ExternalIdLinks
                .AsNoTracking()
                .Where(x =>
                    x.EntityType == ExternalEntityType.SalesInvoice &&
                    x.IsFullyAllocated == false &&
                    x.AppId == appId);

            var totalCount = await baseQuery.CountAsync(ct);

            var items = await baseQuery
                .OrderBy(x => x.LastAllocationCheckUtc == null)  // NULLs first
                .ThenBy(x => x.LastAllocationCheckUtc)
                .ThenBy(x => x.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new PaymentsExportJobDto
                {
                    AppId = x.AppId,
                    ExternalRef = x.ExternalRef,
                    SageUrn = x.SageUrn,
                    AllocatedValue = x.AllocatedValue,
                    OutstandingValue = x.OutstandingValue,
                    LastAllocationCheckUtc = x.LastAllocationCheckUtc,
                    LastAllocationChangeUtc = x.LastAllocationChangeUtc
                })
                .ToListAsync(ct);

            _logger.LogInformation(
                "PaymentsExportJobs: appId={AppId}, pageNumber={PageNumber}, pageSize={PageSize}, totalCount={TotalCount}, returned={Returned}",
                appId, pageNumber, pageSize, totalCount, items.Count);

            var result = new PagedResult<PaymentsExportJobDto>(items, totalCount, pageNumber, pageSize);
            return Ok(result);
        }
    }
}
