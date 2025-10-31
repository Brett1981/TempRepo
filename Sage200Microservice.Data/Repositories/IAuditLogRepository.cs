using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository interface for audit log operations
    /// </summary>
    public interface IAuditLogRepository : IRepository<AuditLog>
    {
        /// <summary>
        /// Gets a filtered and paginated list of audit logs
        /// </summary>
        /// <param name="startDate">     Filter by start date </param>
        /// <param name="endDate">       Filter by end date </param>
        /// <param name="eventTypes">    Filter by event types </param>
        /// <param name="categories">    Filter by categories </param>
        /// <param name="severities">    Filter by severities </param>
        /// <param name="statuses">      Filter by statuses </param>
        /// <param name="userId">        Filter by user ID </param>
        /// <param name="clientId">      Filter by client ID </param>
        /// <param name="ipAddress">     Filter by IP address </param>
        /// <param name="resource">      Filter by resource </param>
        /// <param name="action">        Filter by action </param>
        /// <param name="correlationId"> Filter by correlation ID </param>
        /// <param name="searchTerm">    Search term for description or details </param>
        /// <param name="page">          The page number (1-based) </param>
        /// <param name="pageSize">      The number of items per page </param>
        /// <param name="sortBy">        The property name to sort by </param>
        /// <param name="sortDirection"> The sort direction (asc or desc) </param>
        /// <returns> A filtered and paginated list of audit logs </returns>
        Task<(IEnumerable<AuditLog> Items, int TotalCount)> GetFilteredPagedAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            IEnumerable<AuditEventType> eventTypes = null,
            IEnumerable<AuditEventCategory> categories = null,
            IEnumerable<AuditEventSeverity> severities = null,
            IEnumerable<AuditEventStatus> statuses = null,
            string userId = null,
            string clientId = null,
            string ipAddress = null,
            string resource = null,
            string action = null,
            string correlationId = null,
            string searchTerm = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "Timestamp",
            string sortDirection = "desc");

        /// <summary>
        /// Gets audit logs by correlation ID
        /// </summary>
        /// <param name="correlationId"> The correlation ID </param>
        /// <returns> A list of audit logs with the specified correlation ID </returns>
        Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId);

        /// <summary>
        /// Gets audit logs for a specific resource
        /// </summary>
        /// <param name="resource">    The resource name </param>
        /// <param name="referenceId"> The reference ID </param>
        /// <returns> A list of audit logs for the specified resource </returns>
        Task<IEnumerable<AuditLog>> GetByResourceAsync(string resource, string referenceId);

        /// <summary>
        /// Gets audit logs for a specific client
        /// </summary>
        /// <param name="clientId"> The client ID </param>
        /// <param name="limit">    The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified client </returns>
        Task<IEnumerable<AuditLog>> GetByClientIdAsync(string clientId, int limit = 100);

        /// <summary>
        /// Gets audit logs for a specific user
        /// </summary>
        /// <param name="userId"> The user ID </param>
        /// <param name="limit">  The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified user </returns>
        Task<IEnumerable<AuditLog>> GetByUserIdAsync(string userId, int limit = 100);

        /// <summary>
        /// Gets audit logs for a specific IP address
        /// </summary>
        /// <param name="ipAddress"> The IP address </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified IP address </returns>
        Task<IEnumerable<AuditLog>> GetByIpAddressAsync(string ipAddress, int limit = 100);

        /// <summary>
        /// Gets audit logs by event type
        /// </summary>
        /// <param name="eventType"> The event type </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified event type </returns>
        Task<IEnumerable<AuditLog>> GetByEventTypeAsync(AuditEventType eventType, int limit = 100);

        /// <summary>
        /// Gets audit logs by status
        /// </summary>
        /// <param name="status"> The status </param>
        /// <param name="limit">  The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified status </returns>
        Task<IEnumerable<AuditLog>> GetByStatusAsync(AuditEventStatus status, int limit = 100);

        /// <summary>
        /// Gets audit logs by severity
        /// </summary>
        /// <param name="severity"> The severity </param>
        /// <param name="limit">    The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified severity </returns>
        Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditEventSeverity severity, int limit = 100);

        /// <summary>
        /// Gets audit logs by date range
        /// </summary>
        /// <param name="startDate"> The start date </param>
        /// <param name="endDate">   The end date </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs within the specified date range </returns>
        Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int limit = 100);

        /// <summary>
        /// Gets expired audit logs
        /// </summary>
        /// <returns> A list of expired audit logs </returns>
        Task<IEnumerable<AuditLog>> GetExpiredAsync();

        /// <summary>
        /// Deletes expired audit logs
        /// </summary>
        /// <returns> The number of deleted logs </returns>
        Task<int> DeleteExpiredAsync();

        /// <summary>
        /// Gets audit log statistics
        /// </summary>
        /// <param name="startDate"> The start date </param>
        /// <param name="endDate">   The end date </param>
        /// <returns> Audit log statistics </returns>
        Task<AuditLogStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// Gets a single audit log by its ID.
        /// </summary>
        Task<AuditLog> GetByIdAsync(long id);
    }

    /// <summary>
    /// Represents audit log statistics
    /// </summary>
    public class AuditLogStatistics
    {
        /// <summary>
        /// Gets or sets the total number of audit logs
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the count by event type
        /// </summary>
        public Dictionary<AuditEventType, int> CountByEventType { get; set; }

        /// <summary>
        /// Gets or sets the count by category
        /// </summary>
        public Dictionary<AuditEventCategory, int> CountByCategory { get; set; }

        /// <summary>
        /// Gets or sets the count by severity
        /// </summary>
        public Dictionary<AuditEventSeverity, int> CountBySeverity { get; set; }

        /// <summary>
        /// Gets or sets the count by status
        /// </summary>
        public Dictionary<AuditEventStatus, int> CountByStatus { get; set; }

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
        public Dictionary<DateTime, int> CountByDay { get; set; }
    }
}