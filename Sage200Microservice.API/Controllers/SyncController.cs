using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sage200Microservice.API.Attributes;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Common;       // For PaginatedDataResponse
using Sage200Microservice.Services.Models.Reconciliation; // For Reconciliation DTOs
using Sage200Microservice.Services.Models.Sync;          // For Sync DTOs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        /// Accepts a request to trigger a background synchronization process with Sage.
        /// Intended for scheduled callers like Airflow. Returns immediately with a CorrelationId for tracking.
        /// Requires a valid X-Api-Key header.
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
            string correlationId = HttpContext.TraceIdentifier;
            string apiKeyHeader = Request.Headers["X-Api-Key"].ToString();
            ApiKey? apiKeyEntity = null; // Declare here for audit logging scope

            try
            {
                // Resolve ApiKey entity (needed for audit logging even if service resolves internally)
                if (!string.IsNullOrWhiteSpace(apiKeyHeader))
                {
                    apiKeyEntity = await _apiKeyRepository.GetByKeyAsync(apiKeyHeader) ??
                                   await _apiKeyRepository.GetByPreviousKeyAsync(apiKeyHeader);
                }

                if (apiKeyEntity == null)
                {
                    _logger.LogWarning("FetchSageUpdatesAsync: Middleware allowed request but ApiKey entity not found. Key Header: '{ApiKeyHeader}'", apiKeyHeader);
                    // Relying on middleware for 401, but maybe return 400 here if resolution fails post-auth?
                    return BadRequest(new ProblemDetails { Title = "API Key resolution failed post-authentication.", Status = StatusCodes.Status400BadRequest });
                }

                // Extract headers and create RequestContext
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
                    _logger.LogWarning("FetchSageUpdatesAsync rejected: {Detail}. CorrelationId: {CorrelationId}", detail, correlationId);
                    await LogAuditAsync(AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, detail, ct); // Log failure
                    return BadRequest(new ProblemDetails { Title = "Missing Sage Context", Status = StatusCodes.Status400BadRequest, Detail = detail });
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null;

                var requestContext = new RequestContext(
                    SiteId: siteId,
                    CompanyId: companyId,
                    IdempotencyKey: idempotencyKey,
                    CorrelationId: correlationId
                );

                // Log Audit Attempt (Now includes resolved ApiKey)
                await LogAuditAsync(AuditEventStatus.InProgress, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, "Attempting to trigger Sage update sync.", ct);

                // Call service with the correct signature (apiKeyId/appId resolved internally by service now)
                var result = await _syncService.RequestSyncAsync(request, requestContext, ct);

                // Log Audit Success
                await LogAuditAsync(AuditEventStatus.Success, "FetchSageUpdates", apiKeyEntity, correlationId, request, result, "Successfully queued Sage update sync.", ct);

                return Accepted(result); // Returns 202
            }
            catch (ValidationException vex)
            {
                _logger.LogWarning(vex, "Validation error during FetchSageUpdatesAsync for CorrelationId {CorrelationId}", correlationId);
                await LogAuditAsync(AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, $"Validation failed: {vex.Message}", ct);
                // Return ValidationProblemDetails for consistency if possible
                var errors = new Dictionary<string, string[]> { { vex.Source ?? "Validation", new[] { vex.Message } } };
                return ValidationProblem(new ValidationProblemDetails(errors) { Title = "Validation Error", Status = StatusCodes.Status400BadRequest });
            }
            catch (HttpRequestException hex)
            {
                _logger.LogError(hex, "Upstream HTTP error during FetchSageUpdatesAsync for CorrelationId {CorrelationId}", correlationId);
                await LogAuditAsync(AuditEventStatus.Denied, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, $"Upstream communication error: {hex.StatusCode} - {hex.Message}", ct);
                var statusCode = hex.StatusCode ?? HttpStatusCode.BadGateway;
                return StatusCode((int)statusCode, new ProblemDetails { Title = "Upstream Service Error", Status = (int)statusCode, Detail = hex.Message });
            }
            catch (NotImplementedException)
            {
                _logger.LogError("FetchSageUpdatesAsync called but SyncService logic is not implemented (Stage 9).");
                await LogAuditAsync(AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, "Endpoint called but service logic not implemented.", ct);
                return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails { Title = "Service Not Implemented", Status = StatusCodes.Status501NotImplemented, Detail = "Synchronization logic is pending implementation." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during FetchSageUpdatesAsync for CorrelationId {CorrelationId}", correlationId);
                await LogAuditAsync(AuditEventStatus.Failure, "FetchSageUpdates", apiKeyEntity, correlationId, request, null, $"Internal error: {ex.Message}", ct);
                throw; // Re-throw for global handler
            }
        }

        /// <summary>
        /// Retrieves reconciled Sage data stored locally, linked via ExternalIdLink.
        /// Requires a valid X-Api-Key header. Data is scoped based on the ApiKeyId.
        /// </summary>
        [HttpGet("reconciliation-data")]
        [ProducesResponseType(typeof(PaginatedDataResponse<ReconciliationDataDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status424FailedDependency)]
        public async Task<IActionResult> GetReconciliationDataAsync([FromQuery] ReconciliationQueryParameters queryParams, CancellationToken ct)
        {
            string correlationId = HttpContext.TraceIdentifier;
            string apiKeyHeader = Request.Headers["X-Api-Key"].ToString();
            ApiKey? apiKeyEntity = null; // Declare here for audit logging scope

            try
            {
                // Resolve ApiKey entity (needed for scoping and auditing)
                if (!string.IsNullOrWhiteSpace(apiKeyHeader))
                {
                    apiKeyEntity = await _apiKeyRepository.GetByKeyAsync(apiKeyHeader) ??
                                   await _apiKeyRepository.GetByPreviousKeyAsync(apiKeyHeader);
                }

                if (apiKeyEntity == null)
                {
                    _logger.LogWarning("GetReconciliationDataAsync: Middleware allowed request but ApiKey entity not found. Key Header: '{ApiKeyHeader}'", apiKeyHeader);
                    return BadRequest(new ProblemDetails { Title = "API Key resolution failed post-authentication.", Status = StatusCodes.Status400BadRequest });
                }

                int apiKeyId = apiKeyEntity.Id; // Use ApiKey.Id for scoping via ExternalIdLink.AppId

                // Log Audit Attempt (DataAccess)
                await LogAuditAsync(AuditEventStatus.InProgress, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null, "Attempting to retrieve reconciliation data.", ct, AuditEventType.DataAccess);

                // Call service with apiKeyId (service now resolves AppId internally if needed or uses apiKeyId for scoping)
                var result = await _reconciliationService.GetReconciliationDataAsync(queryParams, apiKeyId, ct);

                // Log Audit Success
                await LogAuditAsync(AuditEventStatus.Success, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, new { result.Metadata.TotalCount }, $"Retrieved {result.Items.Count}/{result.Metadata.TotalCount} records.", ct, AuditEventType.DataAccess);

                return Ok(result);
            }
            catch (ValidationException vex)
            {
                _logger.LogWarning(vex, "Validation error during GetReconciliationDataAsync for CorrelationId {CorrelationId}", correlationId);
                await LogAuditAsync(AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null, $"Validation failed: {vex.Message}", ct, AuditEventType.DataAccess);
                var errors = new Dictionary<string, string[]> { { vex.Source ?? "Validation", new[] { vex.Message } } };
                return ValidationProblem(new ValidationProblemDetails(errors) { Title = "Validation Error", Status = StatusCodes.Status400BadRequest });
            }
            catch (NotImplementedException)
            {
                _logger.LogError("GetReconciliationDataAsync called but ReconciliationService logic is not implemented (Stage 9).");
                await LogAuditAsync(AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null, "Endpoint called but service logic not implemented.", ct, AuditEventType.DataAccess);
                return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails { Title = "Service Not Implemented", Status = StatusCodes.Status501NotImplemented, Detail = "Reconciliation query logic is pending implementation." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during GetReconciliationDataAsync for CorrelationId {CorrelationId}", correlationId);
                await LogAuditAsync(AuditEventStatus.Failure, "GetReconciliationData", apiKeyEntity, correlationId, queryParams, null, $"Internal error: {ex.Message}", ct, AuditEventType.DataAccess);
                throw; // Re-throw for global handler
            }
        }

        // Consolidated Audit Logging Helper
        private async Task LogAuditAsync(
            AuditEventStatus status,
            string action,
            ApiKey? apiKey, // Make nullable as resolution might fail before audit
            string correlationId,
            object? requestPayload,
            object? responsePayload,
            string description,
            CancellationToken ct,
            AuditEventType eventType = AuditEventType.DataModification) // Default to DataModification
        {
            // Avoid logging if ApiKey resolution failed earlier and we don't have the entity
            string clientId = apiKey?.ClientName ?? "Unknown";
            string userId = "API"; // Or derive from ApiKey if applicable

            // Choose appropriate AuditLogService method based on event type
            try
            {
                if (eventType == AuditEventType.DataAccess)
                {
                    await _audit.LogDataAccessEventAsync(
                         userId: userId,
                         clientId: clientId,
                         ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                         resource: $"SyncController/{action}",
                         referenceId: correlationId, // Use CorrelationId as reference
                         referenceName: "CorrelationId",
                         action: action,
                         status: status,
                         description: description,
                         details: requestPayload, // Log query params for data access
                         correlationId: correlationId,
                         cancellationToken: ct);
                }
                else // Default to DataModification
                {
                    await _audit.LogDataModificationEventAsync(
                        userId: userId,
                        clientId: clientId,
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        resource: $"SyncController/{action}",
                        referenceId: correlationId, // Use CorrelationId as reference
                        referenceName: "CorrelationId",
                        action: action,
                        status: status,
                        description: description,
                        details: new { Request = requestPayload, Response = responsePayload }, // Log request/response for modifications
                        correlationId: correlationId,
                        cancellationToken: ct);
                }
            }
            catch (MissingMethodException mmex) // Specific catch for LogDataAccessEventAsync if missing
            {
                _logger.LogWarning(mmex, "AuditLogService may be missing the {ExpectedMethod} method. Audit log for {Action} skipped.",
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

