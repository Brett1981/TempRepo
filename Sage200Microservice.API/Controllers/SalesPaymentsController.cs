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

        /// <summary>
        /// Constructs the controller with required dependencies.
        /// </summary>
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
            // Prometheus: track active POST /api/SalesPayments requests
            using var _ = _apiMetrics.TrackApiRequest("POST", "/api/SalesPayments");
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

        /// <summary>
        /// Refreshes allocation values for candidate SalesInvoices and returns a paged result of updates.
        /// Spec §6.5.2: For each candidate invoice (not fully allocated), query Sage for allocation state,
        /// update local fields, and return the updated page. Tenant-scoped by AppId from X-Api-Key.
        /// </summary>
        /// <param name="pageNumber">1-based page number (default 1).</param>
        /// <param name="pageSize">Requested page size (max 200, default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>PagedResult of <see cref="PaymentsAllocationCheckDto"/>.</returns>
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

            // Resolve AppId (same pattern as export-jobs)
            var apiKey = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }

            var keyRow = await _apiKeys.GetByKeyAsync(apiKey) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey);
            var valid = await _apiKeys.IsValidKeyAsync(apiKey);
            if (keyRow == null || !valid)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }
            await _apiKeys.UpdateLastUsedAsync(apiKey);
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
                // Default result shell for this row
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
                    // Sage error — continue, do not fail whole page
                    row.StatusMessage = status;
                    failures++;
                }
                else
                {
                    // Compare & update values
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

                    // Project refreshed values back to DTO
                    row.AllocatedValue = link.AllocatedValue;
                    row.OutstandingValue = link.OutstandingValue;
                    row.LastAllocationCheckUtc = link.LastAllocationCheckUtc;
                    row.LastAllocationChangeUtc = link.LastAllocationChangeUtc;
                }

                results.Add(row);
            }

            // Stage TransactionAttempt and persist everything in one atomic operation
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

            // Commit all DB changes together (ExternalIdLink updates  TransactionAttempt)
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            _logger.LogInformation(
                "AllocationsCheck: appId={AppId}, pageNumber={PageNumber}, pageSize={PageSize}, totalCount={TotalCount}, updated={Updated}, failures={Failures}",
                appId, pageNumber, pageSize, totalCount, updated, failures);

            var page = new PagedResult<PaymentsAllocationCheckDto>(results, totalCount, pageNumber, pageSize);
            return Ok(page);
        }

        /// <summary>
        /// Returns a paginated list of SalesInvoice allocation candidates for the Airflow export job.
        /// Spec §6.3.1. Candidates are scoped by the caller’s AppId (resolved from X-Api-Key),
        /// and filtered to invoices that are not fully allocated:
        ///   WHERE EntityType == ExternalEntityType.SalesInvoice
        ///     AND (IsFullyAllocated == false OR IsFullyAllocated IS NULL)
        ///     AND AppId == {resolved AppId}
        /// Sorted by LastAllocationCheckUtc (NULLS FIRST) then Id ascending for stable pagination.
        /// </summary>
        /// <param name="pageNumber">1-based page number (default 1).</param>
        /// <param name="pageSize">Requested page size (max 200, default 50).</param>
        /// <returns>PagedResult of <see cref="PaymentsExportJobDto"/>.</returns>
        [HttpGet("export-jobs")]
        [SageRoutingHeaders(DocumentApiKey = true)]
        public async Task<ActionResult<PagedResult<PaymentsExportJobDto>>> GetPaymentsExportJobsAsync(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50)
        {
            using var _ = _apiMetrics.TrackApiRequest("GET", "/api/SalesPayments/export-jobs");
            // Clamp paging as per confirmation.
            const int MaxPageSize = 200;
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            // Resolve AppId using the current repository pattern (per SalesInvoicesController).
            var apiKey = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }

            var keyRow = await _apiKeys.GetByKeyAsync(apiKey) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey);
            var valid = await _apiKeys.IsValidKeyAsync(apiKey);
            if (keyRow == null || !valid)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "API key could not be resolved to a valid AppId."
                });
            }
            await _apiKeys.UpdateLastUsedAsync(apiKey);
            var appId = keyRow.Id;

            // Base candidate query with tenant scoping and allocation filter (Spec §6.5.1 / §8.5).
            var baseQuery = _db.ExternalIdLinks
                .AsNoTracking()
                .Where(x =>
                    x.EntityType == ExternalEntityType.SalesInvoice &&
                    (x.IsFullyAllocated == false) &&
                    x.AppId == appId);

            var totalCount = await baseQuery.CountAsync();

            // NULLS FIRST then Id ASC for stable paging.
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
                .ToListAsync();

            _logger.LogInformation(
                "PaymentsExportJobs: appId={AppId}, pageNumber={PageNumber}, pageSize={PageSize}, totalCount={TotalCount}, returned={Returned}",
                appId, pageNumber, pageSize, totalCount, items.Count);

            var result = new PagedResult<PaymentsExportJobDto>(items, totalCount, pageNumber, pageSize);
            return Ok(result);
        }
    }
}
