using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Common;          // PaginatedDataResponse
using Sage200Microservice.Services.Models.Reconciliation;  // Reconciliation DTOs
using Sage200Microservice.Services.Models.Sync;            // Sync DTOs
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Endpoints related to triggering synchronization with Sage and querying reconciled data.
    /// </summary>
    [ApiController]
    [Route("api/sync")]
    [Produces("application/json")]
    public partial class SyncController : ControllerBase
    {
        private readonly ILogger<SyncController> _logger;
        private readonly ISyncService _syncService;
        private readonly IReconciliationService _reconciliationService;
        private readonly IAuditLogService _audit;
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly SageApiSettings _sageApiSettings;

        public SyncController(
            ILogger<SyncController> logger,
            ISyncService syncService,
            IReconciliationService reconciliationService,
            IAuditLogService audit,
            IApiKeyRepository apiKeyRepository,
            IOptions<SageApiSettings> sageApiOptions)
        {
            _logger = logger;
            _syncService = syncService;
            _reconciliationService = reconciliationService;
            _audit = audit;
            _apiKeyRepository = apiKeyRepository;
            _sageApiSettings = sageApiOptions.Value;
        }

        /// <summary>
        /// Triggers a background synchronization with Sage (returns 202 + correlation id).
        /// Requires a valid X-Api-Key; advertises X-Site/X-Company/Idempotency-Key.
        /// </summary>
        [HttpPost("fetch-sage-updates")]
        [Consumes("application/json")]
        [SageRoutingHeaders(RequiresIdempotencyKey = true)]
        [ProducesResponseType(typeof(SyncInitiatedResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status424FailedDependency)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> FetchSageUpdatesAsync([FromBody] FetchSageUpdatesRequest request, CancellationToken ct)
        {
            var correlationId = HttpContext.TraceIdentifier;
            var apiKeyHeader = Request.Headers["X-Api-Key"].ToString();
            ApiKey? apiKeyEntity = null;

            try
            {
                // Resolve ApiKey entity (explicitly pass ct)
                if (!string.IsNullOrWhiteSpace(apiKeyHeader))
                {
                    apiKeyEntity = await _apiKeyRepository.GetByKeyAsync(apiKeyHeader, ct)
                                   ?? await _apiKeyRepository.GetByPreviousKeyAsync(apiKeyHeader, ct);
                }

                if (apiKeyEntity == null)
                {
                    _logger.LogWarning("FetchSageUpdatesAsync: API key entity not found. Header: '{ApiKeyHeader}', CorrId={CorrelationId}",
                        apiKeyHeader, correlationId);

                    await LogAuditAsync(
                        AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                        "API Key resolution failed post-authentication.", ct);

                    return BadRequest(new ProblemDetails
                    {
                        Title = "API Key resolution failed post-authentication.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                // Extract routing context (with config fallbacks)
                string? siteId = Request.Headers["X-Site"].ToString();
                string? companyId = Request.Headers["X-Company"].ToString();
                string? idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(siteId)) siteId = _sageApiSettings.SiteId;
                if (string.IsNullOrWhiteSpace(companyId)) companyId = _sageApiSettings.CompanyId;

                if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(companyId))
                {
                    var missing = new List<string>();
                    if (string.IsNullOrWhiteSpace(siteId)) missing.Add("X-Site/Config");
                    if (string.IsNullOrWhiteSpace(companyId)) missing.Add("X-Company/Config");

                    var detail = $"Missing Sage context: {string.Join(", ", missing)}.";
                    _logger.LogWarning("FetchSageUpdatesAsync rejected: {Detail}. CorrId={CorrelationId}", detail, correlationId);

                    await LogAuditAsync(
                        AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, detail, ct);

                    return BadRequest(new ProblemDetails
                    {
                        Title = "Missing Sage Context",
                        Detail = detail,
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null;

                var context = new RequestContext(
                    SiteId: siteId,
                    CompanyId: companyId,
                    IdempotencyKey: idempotencyKey,
                    CorrelationId: correlationId);

                await LogAuditAsync(
                    AuditEventStatus.InProgress, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                    "Attempting to trigger Sage update sync.", ct);

                var result = await _syncService.RequestSyncAsync(request, context, ct);

                await LogAuditAsync(
                    AuditEventStatus.Success, "FetchSageUpdates", apiKeyEntity, correlationId, request, result,
                    "Successfully queued Sage update sync.", ct);

                return Accepted(result);
            }
            catch (ValidationException vex)
            {
                _logger.LogWarning(vex, "Validation error during FetchSageUpdatesAsync. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                    $"Validation failed: {vex.Message}", ct);

                var errors = new Dictionary<string, string[]>
                {
                    { vex.Source ?? "Validation", new[] { vex.Message } }
                };
                return ValidationProblem(new ValidationProblemDetails(errors)
                {
                    Title = "Validation Error",
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (HttpRequestException hex)
            {
                _logger.LogError(hex, "Upstream HTTP error during FetchSageUpdatesAsync. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Denied, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                    $"Upstream communication error: {hex.StatusCode} - {hex.Message}", ct);

                var statusCode = (int)(hex.StatusCode ?? HttpStatusCode.BadGateway);
                return StatusCode(statusCode, new ProblemDetails
                {
                    Title = "Upstream Service Error",
                    Detail = hex.Message,
                    Status = statusCode
                });
            }
            catch (NotImplementedException)
            {
                _logger.LogError("FetchSageUpdatesAsync called but SyncService logic is not implemented. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                    "Endpoint called but service logic not implemented.", ct);

                return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
                {
                    Title = "Service Not Implemented",
                    Detail = "Synchronization logic is pending implementation.",
                    Status = StatusCodes.Status501NotImplemented
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during FetchSageUpdatesAsync. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null,
                    $"Internal error: {ex.Message}", ct);
                throw; // bubble to global handler
            }
        }

        /// <summary>
        /// Retrieves reconciled Sage data stored locally, scoped by ApiKey (resolved from X-Api-Key).
        /// </summary>
        [HttpGet("reconciliation-data")]
        [SageRoutingHeaders(DocumentApiKey = true)]
        [ProducesResponseType(typeof(PaginatedDataResponse<ReconciliationDataDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status424FailedDependency)]
        public async Task<IActionResult> GetReconciliationDataAsync([FromQuery] ReconciliationQueryParameters queryParams, CancellationToken ct)
        {
            var correlationId = HttpContext.TraceIdentifier;
            var apiKeyHeader = Request.Headers["X-Api-Key"].ToString();
            ApiKey? apiKeyEntity = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(apiKeyHeader))
                {
                    apiKeyEntity = await _apiKeyRepository.GetByKeyAsync(apiKeyHeader, ct)
                                   ?? await _apiKeyRepository.GetByPreviousKeyAsync(apiKeyHeader, ct);
                }

                if (apiKeyEntity == null)
                {
                    _logger.LogWarning("GetReconciliationDataAsync: API key entity not found. Header: '{ApiKeyHeader}', CorrId={CorrelationId}",
                        apiKeyHeader, correlationId);

                    return BadRequest(new ProblemDetails
                    {
                        Title = "API Key resolution failed post-authentication.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                var apiKeyId = apiKeyEntity.Id;

                await LogAuditAsync(
                    AuditEventStatus.InProgress, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null,
                    "Attempting to retrieve reconciliation data.", ct, AuditEventType.DataAccess);

                var result = await _reconciliationService.GetReconciliationDataAsync(queryParams, apiKeyId, ct);

                await LogAuditAsync(
                    AuditEventStatus.Success, "GetReconciliationData", apiKeyEntity, correlationId, queryParams,
                    new { result.Metadata.TotalCount },
                    $"Retrieved {result.Items.Count}/{result.Metadata.TotalCount} records.", ct, AuditEventType.DataAccess);

                return Ok(result);
            }
            catch (ValidationException vex)
            {
                _logger.LogWarning(vex, "Validation error during GetReconciliationDataAsync. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null,
                    $"Validation failed: {vex.Message}", ct, AuditEventType.DataAccess);

                var errors = new Dictionary<string, string[]>
                {
                    { vex.Source ?? "Validation", new[] { vex.Message } }
                };
                return ValidationProblem(new ValidationProblemDetails(errors)
                {
                    Title = "Validation Error",
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (NotImplementedException)
            {
                _logger.LogError("GetReconciliationDataAsync called but service logic not implemented. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null,
                    "Endpoint called but service logic not implemented.", ct, AuditEventType.DataAccess);

                return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
                {
                    Title = "Service Not Implemented",
                    Detail = "Reconciliation query logic is pending implementation.",
                    Status = StatusCodes.Status501NotImplemented
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during GetReconciliationDataAsync. CorrId={CorrelationId}", correlationId);
                await LogAuditAsync(
                    AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null,
                    $"Internal error: {ex.Message}", ct, AuditEventType.DataAccess);
                throw;
            }
        }

        // -------- Consolidated Audit Logging Helper --------
        private async Task LogAuditAsync(
            AuditEventStatus status,
            string action,
            ApiKey? apiKey,
            string correlationId,
            object? requestPayload,
            object? responsePayload,
            string description,
            CancellationToken ct,
            AuditEventType eventType = AuditEventType.DataModification)
        {
            var clientId = apiKey?.ClientName ?? "Unknown";
            var userId = "API";

            try
            {
                if (eventType == AuditEventType.DataAccess)
                {
                    await _audit.LogDataAccessEventAsync(
                        userId: userId,
                        clientId: clientId,
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        resource: $"SyncController/{action}",
                        referenceId: correlationId,
                        referenceName: "CorrelationId",
                        action: action,
                        status: status,
                        description: description,
                        details: requestPayload,
                        correlationId: correlationId,
                        cancellationToken: ct);
                }
                else
                {
                    await _audit.LogDataModificationEventAsync(
                        userId: userId,
                        clientId: clientId,
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        resource: $"SyncController/{action}",
                        referenceId: correlationId,
                        referenceName: "CorrelationId",
                        action: action,
                        status: status,
                        description: description,
                        previousState: null,
                        newState: null,
                        details: new { Request = requestPayload, Response = responsePayload },
                        correlationId: correlationId,
                        cancellationToken: ct);
                }
            }
            catch (MissingMethodException mmex)
            {
                _logger.LogWarning(mmex,
                    "AuditLogService may be missing the {ExpectedMethod} method. Audit log for {Action} skipped.",
                    eventType == AuditEventType.DataAccess ? "LogDataAccessEventAsync" : "LogDataModificationEventAsync",
                    action);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log for action {Action} with CorrelationId {CorrelationId}", action, correlationId);
            }
        }
    }
}
