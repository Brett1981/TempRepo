using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for audit log responses
    /// </summary>
    public class AuditLogResponseDto
    {
        /// <summary>
        /// Gets or sets the audit log ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the event type
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// Gets or sets the category
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the severity
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the user ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Gets or sets the client ID
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets the IP address
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Gets or sets the resource
        /// </summary>
        public string Resource { get; set; }

        /// <summary>
        /// Gets or sets the action
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Gets or sets the status
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Gets or sets the description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the details
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Gets or sets the correlation ID
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Gets or sets the HTTP method
        /// </summary>
        public string HttpMethod { get; set; }

        /// <summary>
        /// Gets or sets the URL path
        /// </summary>
        public string UrlPath { get; set; }

        /// <summary>
        /// Gets or sets the HTTP status code
        /// </summary>
        public int? HttpStatusCode { get; set; }

        /// <summary>
        /// Gets or sets the duration in milliseconds
        /// </summary>
        public long? DurationMs { get; set; }

        /// <summary>
        /// Gets or sets the user agent
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Gets or sets the reference ID
        /// </summary>
        public string ReferenceId { get; set; }

        /// <summary>
        /// Gets or sets the reference name
        /// </summary>
        public string ReferenceName { get; set; }

        /// <summary>
        /// Gets or sets the previous state
        /// </summary>
        public string PreviousState { get; set; }

        /// <summary>
        /// Gets or sets the new state
        /// </summary>
        public string NewState { get; set; }
    }

    /// <summary>
    /// DTO for audit log search requests
    /// </summary>
    public class AuditLogSearchRequestDto
    {
        /// <summary>
        /// Gets or sets the start date
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// Gets or sets the end date
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Gets or sets the event types
        /// </summary>
        public List<string> EventTypes { get; set; }

        /// <summary>
        /// Gets or sets the categories
        /// </summary>
        public List<string> Categories { get; set; }

        /// <summary>
        /// Gets or sets the severities
        /// </summary>
        public List<string> Severities { get; set; }

        /// <summary>
        /// Gets or sets the statuses
        /// </summary>
        public List<string> Statuses { get; set; }

        /// <summary>
        /// Gets or sets the user ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Gets or sets the client ID
        /// </summary>
        public string? ClientId { get; set; } = "AuthFlow";

        /// <summary>
        /// Gets or sets the IP address
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Gets or sets the resource
        /// </summary>
        public string Resource { get; set; }

        /// <summary>
        /// Gets or sets the action
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Gets or sets the correlation ID
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Gets or sets the search term
        /// </summary>
        public string SearchTerm { get; set; }

        /// <summary>
        /// Gets or sets the page number (1-based)
        /// </summary>
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        /// <summary>
        /// Gets or sets the page size
        /// </summary>
        [Range(1, 100)]
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Gets or sets the property name to sort by
        /// </summary>
        public string SortBy { get; set; } = "Timestamp";

        /// <summary>
        /// Gets or sets the sort direction (asc or desc)
        /// </summary>
        public string SortDirection { get; set; } = "desc";
    }

    /// <summary>
    /// DTO for paginated responses
    /// </summary>
    /// <typeparam name="T"> The item type </typeparam>
    public class PaginatedResponse<T>
    {
        /// <summary>
        /// Gets or sets the items
        /// </summary>
        public List<T> Items { get; set; }

        /// <summary>
        /// Gets or sets the total count
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the page number
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Gets or sets the page size
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets or sets the total number of pages
        /// </summary>
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// DTO for audit log statistics responses
    /// </summary>
    public class AuditLogStatisticsResponseDto
    {
        /// <summary>
        /// Gets or sets the total count
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the count by event type
        /// </summary>
        public Dictionary<string, int> CountByEventType { get; set; }

        /// <summary>
        /// Gets or sets the count by category
        /// </summary>
        public Dictionary<string, int> CountByCategory { get; set; }

        /// <summary>
        /// Gets or sets the count by severity
        /// </summary>
        public Dictionary<string, int> CountBySeverity { get; set; }

        /// <summary>
        /// Gets or sets the count by status
        /// </summary>
        public Dictionary<string, int> CountByStatus { get; set; }

        /// <summary>
        /// Gets or sets the count by resource
        /// </summary>
        public Dictionary<string, int> CountByResource { get; set; }

        /// <summary>
        /// Gets or sets the count by action
        /// </summary>
        public Dictionary<string, int> CountByAction { get; set; }

        /// <summary>
        /// Gets or sets the count by client ID
        /// </summary>
        public Dictionary<string, int> CountByClientId { get; set; }

        /// <summary>
        /// Gets or sets the count by user ID
        /// </summary>
        public Dictionary<string, int> CountByUserId { get; set; }

        /// <summary>
        /// Gets or sets the count by IP address
        /// </summary>
        public Dictionary<string, int> CountByIpAddress { get; set; }

        /// <summary>
        /// Gets or sets the count by day
        /// </summary>
        public Dictionary<string, int> CountByDay { get; set; }
    }
}