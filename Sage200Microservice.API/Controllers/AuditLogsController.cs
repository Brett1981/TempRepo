using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.API.Middleware;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Controller for audit logs
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AuditLogsController : ControllerBase
    {
        private readonly ILogger<AuditLogsController> _logger;
        private readonly IAuditLogService _auditLogService;

        public AuditLogsController(
            ILogger<AuditLogsController> logger,
            IAuditLogService auditLogService)
        {
            _logger = logger;
            _auditLogService = auditLogService;
        }

        /// <summary>
        /// Gets a filtered and paginated list of audit logs
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [SkipAudit]
        public async Task<ActionResult<PaginatedResponse<AuditLogResponseDto>>> Search(
            [FromQuery] AuditLogSearchRequestDto request,
            CancellationToken ct)
        {
            try
            {
                // Parse enum filters safely, collect any invalid values to report back
                var invalid = new List<string>();

                List<AuditEventType>? eventTypes = null;
                if (request.EventTypes is { Count: > 0 })
                {
                    eventTypes = new();
                    foreach (var s in request.EventTypes!)
                    {
                        if (Enum.TryParse<AuditEventType>(s, ignoreCase: true, out var v)) eventTypes.Add(v);
                        else invalid.Add($"EventType='{s}'");
                    }
                }

                List<AuditEventCategory>? categories = null;
                if (request.Categories is { Count: > 0 })
                {
                    categories = new();
                    foreach (var s in request.Categories!)
                    {
                        if (Enum.TryParse<AuditEventCategory>(s, true, out var v)) categories.Add(v);
                        else invalid.Add($"Category='{s}'");
                    }
                }

                List<AuditEventSeverity>? severities = null;
                if (request.Severities is { Count: > 0 })
                {
                    severities = new();
                    foreach (var s in request.Severities!)
                    {
                        if (Enum.TryParse<AuditEventSeverity>(s, true, out var v)) severities.Add(v);
                        else invalid.Add($"Severity='{s}'");
                    }
                }

                List<AuditEventStatus>? statuses = null;
                if (request.Statuses is { Count: > 0 })
                {
                    statuses = new();
                    foreach (var s in request.Statuses!)
                    {
                        if (Enum.TryParse<AuditEventStatus>(s, true, out var v)) statuses.Add(v);
                        else invalid.Add($"Status='{s}'");
                    }
                }

                if (invalid.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = "One or more filter values are invalid.",
                        invalid
                    });
                }

                var (logs, totalCount) = await _auditLogService.GetFilteredPagedAsync(
                    startDate: request.StartDate,
                    endDate: request.EndDate,
                    eventTypes: eventTypes,
                    categories: categories,
                    severities: severities,
                    statuses: statuses,
                    userId: request.UserId,
                    clientId: request.ClientId,
                    ipAddress: request.IpAddress,
                    resource: request.Resource,
                    action: request.Action,
                    correlationId: request.CorrelationId,
                    searchTerm: request.SearchTerm,
                    page: request.Page,
                    pageSize: request.PageSize,
                    sortBy: request.SortBy ?? "Timestamp",
                    sortDirection: request.SortDirection ?? "desc"
                );

                var items = logs.Select(MapToResponseDto).ToList();

                var response = new PaginatedResponse<AuditLogResponseDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
                };

                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Audit log Search cancelled by client.");
                return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching audit logs");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while searching audit logs" });
            }
        }

        /// <summary>
        /// Gets an audit log by ID
        /// </summary>
        [HttpGet("{id:long}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SkipAudit]
        public async Task<ActionResult<AuditLogResponseDto>> GetById(long id, CancellationToken ct)
        {
            try
            {
                var auditLog = await _auditLogService.GetByIdAsync(id);
                if (auditLog is null) return NotFound();
                return Ok(MapToResponseDto(auditLog));
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GetById({Id}) cancelled by client.", id);
                return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit log {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while getting the audit log" });
            }
        }

        /// <summary>
        /// Gets audit logs by correlation ID
        /// </summary>
        [HttpGet("correlation/{correlationId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SkipAudit]
        public async Task<ActionResult<List<AuditLogResponseDto>>> GetByCorrelationId(string correlationId, CancellationToken ct)
        {
            try
            {
                var auditLogs = await _auditLogService.GetByCorrelationIdAsync(correlationId);
                var items = auditLogs.Select(MapToResponseDto).ToList();
                return Ok(items);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GetByCorrelationId({CorrelationId}) cancelled by client.", correlationId);
                return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit logs by correlation ID {CorrelationId}", correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while getting audit logs" });
            }
        }

        /// <summary>
        /// Gets audit logs for a specific resource
        /// </summary>
        [HttpGet("resource/{resource}/{referenceId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SkipAudit]
        public async Task<ActionResult<List<AuditLogResponseDto>>> GetByResource(string resource, string referenceId, CancellationToken ct)
        {
            try
            {
                var auditLogs = await _auditLogService.GetByResourceAsync(resource, referenceId);
                var items = auditLogs.Select(MapToResponseDto).ToList();
                return Ok(items);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GetByResource({Resource}, {ReferenceId}) cancelled by client.", resource, referenceId);
                return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit logs for resource {Resource} with ID {ReferenceId}", resource, referenceId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while getting audit logs" });
            }
        }

        /// <summary>
        /// Gets audit log statistics
        /// </summary>
        [HttpGet("statistics")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SkipAudit]
        public async Task<ActionResult<AuditLogStatisticsResponseDto>> GetStatistics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            CancellationToken ct = default)
        {
            try
            {
                var statistics = await _auditLogService.GetStatisticsAsync(startDate, endDate);

                var response = new AuditLogStatisticsResponseDto
                {
                    TotalCount = statistics.TotalCount,
                    CountByEventType = statistics.CountByEventType.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                    CountByCategory = statistics.CountByCategory.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                    CountBySeverity = statistics.CountBySeverity.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                    CountByStatus = statistics.CountByStatus.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                    CountByResource = statistics.CountByResource,
                    CountByAction = statistics.CountByAction,
                    CountByClientId = statistics.CountByClientId,
                    CountByUserId = statistics.CountByUserId,
                    CountByIpAddress = statistics.CountByIpAddress,
                    CountByDay = statistics.CountByDay.ToDictionary(kvp => kvp.Key.ToString("yyyy-MM-dd"), kvp => kvp.Value)
                };

                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GetStatistics cancelled by client.");
                return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit log statistics");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while getting audit log statistics" });
            }
        }

        /// <summary>
        /// Maps an audit log to a response DTO
        /// </summary>
        private static AuditLogResponseDto MapToResponseDto(AuditLog auditLog) => new()
        {
            Id = auditLog.Id,
            Timestamp = auditLog.Timestamp,
            EventType = auditLog.EventType.ToString(),
            Category = auditLog.Category.ToString(),
            Severity = auditLog.Severity.ToString(),
            UserId = auditLog.UserId,
            ClientId = auditLog.ClientId,
            IpAddress = auditLog.IpAddress,
            Resource = auditLog.Resource,
            Action = auditLog.Action,
            Status = auditLog.Status.ToString(),
            Description = auditLog.Description,
            Details = auditLog.Details,
            CorrelationId = auditLog.CorrelationId,
            HttpMethod = auditLog.HttpMethod,
            UrlPath = auditLog.UrlPath,
            HttpStatusCode = auditLog.HttpStatusCode,
            DurationMs = auditLog.DurationMs,
            UserAgent = auditLog.UserAgent,
            ReferenceId = auditLog.ReferenceId,
            ReferenceName = auditLog.ReferenceName,
            PreviousState = auditLog.PreviousState,
            NewState = auditLog.NewState
        };
    }
}
