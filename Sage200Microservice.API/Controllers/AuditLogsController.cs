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
        /// Gets a filtered and paginated list of audit logs.
        /// If no filters are supplied, returns the latest 500 by Timestamp desc.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [SkipAudit]
        public async Task<ActionResult<PaginatedResponse<AuditLogResponseDto>>> Search(CancellationToken ct)
        {
            try
            {
                // Read optional query values (no DTO model binding, no [Required] validation)
                var q = Request.Query;

                // Simple helpers
                static string? S(IQueryCollection q, string key)
                    => q.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;

                static List<string>? L(IQueryCollection q, string key)
                    => q.TryGetValue(key, out var v) && v.Count > 0
                        ? v.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : null;

                static DateTime? D(IQueryCollection q, string key)
                    => DateTime.TryParse(S(q, key), out var dt) ? dt : null;

                static int? I(IQueryCollection q, string key)
                    => int.TryParse(S(q, key), out var iv) ? iv : null;

                // Optional filters
                var startDate = D(q, "startDate");
                var endDate = D(q, "endDate");
                var userId = S(q, "userId");
                var clientId = S(q, "clientId");
                var ipAddress = S(q, "ipAddress");
                var resource = S(q, "resource");
                var action = S(q, "action");
                var correlation = S(q, "correlationId");
                var searchTerm = S(q, "searchTerm");

                var sortBy = S(q, "sortBy") ?? "Timestamp";
                var sortDir = S(q, "sortDirection") ?? "desc";

                var page = I(q, "page") ?? 1;
                var pageSize = I(q, "pageSize") ?? 500; // default latest 500
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 500;
                if (pageSize > 2000) pageSize = 2000; // server cap

                // Enum lists (optional, tolerant, case-insensitive)
                var invalid = new List<string>();

                List<AuditEventType>? eventTypes = TryParseEnumList<AuditEventType>(L(q, "eventTypes"), "EventType", invalid);
                List<AuditEventCategory>? categories = TryParseEnumList<AuditEventCategory>(L(q, "categories"), "Category", invalid);
                List<AuditEventSeverity>? severities = TryParseEnumList<AuditEventSeverity>(L(q, "severities"), "Severity", invalid);
                List<AuditEventStatus>? statuses = TryParseEnumList<AuditEventStatus>(L(q, "statuses"), "Status", invalid);

                if (invalid.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = "One or more filter values are invalid.",
                        invalid
                    });
                }

                // If no filters at all, return latest 500 by Timestamp desc
                var noFilters =
                    startDate is null && endDate is null &&
                    userId is null && clientId is null && ipAddress is null &&
                    resource is null && action is null && correlation is null &&
                    (eventTypes is null || eventTypes.Count == 0) &&
                    (categories is null || categories.Count == 0) &&
                    (severities is null || severities.Count == 0) &&
                    (statuses is null || statuses.Count == 0) &&
                    string.IsNullOrWhiteSpace(searchTerm);

                if (noFilters)
                {
                    // Ask the service for “latest 500” via the generic filtered/paged API
                    var (latest, total) = await _auditLogService.GetFilteredPagedAsync(
                        startDate: null,
                        endDate: null,
                        eventTypes: null,
                        categories: null,
                        severities: null,
                        statuses: null,
                        userId: null,
                        clientId: null,
                        ipAddress: null,
                        resource: null,
                        action: null,
                        correlationId: null,
                        searchTerm: null,
                        page: 1,
                        pageSize: 500,
                        sortBy: "Timestamp",
                        sortDirection: "desc"
                    );

                    var items = latest.Select(MapToResponseDto).ToList();
                    return Ok(new PaginatedResponse<AuditLogResponseDto>
                    {
                        Items = items,
                        TotalCount = total,
                        Page = 1,
                        PageSize = 500,
                        TotalPages = (int)Math.Ceiling(total / 500d)
                    });
                }

                // Otherwise, perform filtered search with whatever was supplied (all optional)
                var (logs, totalCount) = await _auditLogService.GetFilteredPagedAsync(
                    startDate: startDate,
                    endDate: endDate,
                    eventTypes: eventTypes,
                    categories: categories,
                    severities: severities,
                    statuses: statuses,
                    userId: userId,
                    clientId: clientId,
                    ipAddress: ipAddress,
                    resource: resource,
                    action: action,
                    correlationId: correlation,
                    searchTerm: searchTerm,
                    page: page,
                    pageSize: pageSize,
                    sortBy: sortBy,
                    sortDirection: sortDir
                );

                var mapped = logs.Select(MapToResponseDto).ToList();
                return Ok(new PaginatedResponse<AuditLogResponseDto>
                {
                    Items = mapped,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
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

        // ----- helpers -----

        private static List<TEnum>? TryParseEnumList<TEnum>(List<string>? raw, string label, List<string> invalid)
            where TEnum : struct, Enum
        {
            if (raw is null || raw.Count == 0) return null;
            var list = new List<TEnum>();
            foreach (var s in raw)
            {
                if (Enum.TryParse<TEnum>(s, ignoreCase: true, out var v)) list.Add(v);
                else invalid.Add($"{label}='{s}'");
            }
            return list;
        }

        private static AuditLogResponseDto MapToResponseDto(AuditLog a) => new()
        {
            Id = a.Id,
            Timestamp = a.Timestamp,
            EventType = a.EventType.ToString(),
            Category = a.Category.ToString(),
            Severity = a.Severity.ToString(),
            UserId = a.UserId,
            ClientId = a.ClientId,
            IpAddress = a.IpAddress,
            Resource = a.Resource,
            Action = a.Action,
            Status = a.Status.ToString(),
            Description = a.Description,
            Details = a.Details,
            CorrelationId = a.CorrelationId,
            HttpMethod = a.HttpMethod,
            UrlPath = a.UrlPath,
            HttpStatusCode = a.HttpStatusCode,
            DurationMs = a.DurationMs,
            UserAgent = a.UserAgent,
            ReferenceId = a.ReferenceId,
            ReferenceName = a.ReferenceName,
            PreviousState = a.PreviousState,
            NewState = a.NewState
        };
    }
}
