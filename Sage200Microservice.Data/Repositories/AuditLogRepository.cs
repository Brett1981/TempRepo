using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Extensions;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository implementation for audit log operations
    /// </summary>
    public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
    {
        /// <summary>
        /// Initializes a new instance of the AuditLogRepository class
        /// </summary>
        /// <param name="context"> The database context </param>
        public AuditLogRepository(ApplicationContext context) : base(context)
        {
        }

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
        public async Task<(IEnumerable<AuditLog> Items, int TotalCount)> GetFilteredPagedAsync(
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
            string sortDirection = "desc")
        {
            // Start with all audit logs
            var query = _context.Set<AuditLog>().AsQueryable();

            // Apply filters
            if (startDate.HasValue)
            {
                query = query.Where(log => log.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // Include the entire end date (up to 23:59:59)
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(log => log.Timestamp <= endOfDay);
            }

            if (eventTypes != null && eventTypes.Any())
            {
                query = query.Where(log => eventTypes.Contains(log.EventType));
            }

            if (categories != null && categories.Any())
            {
                query = query.Where(log => categories.Contains(log.Category));
            }

            if (severities != null && severities.Any())
            {
                query = query.Where(log => severities.Contains(log.Severity));
            }

            if (statuses != null && statuses.Any())
            {
                query = query.Where(log => statuses.Contains(log.Status));
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(log => log.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(log => log.ClientId == clientId);
            }

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                query = query.Where(log => log.IpAddress == ipAddress);
            }

            if (!string.IsNullOrWhiteSpace(resource))
            {
                query = query.Where(log => log.Resource == resource);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(log => log.Action == action);
            }

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                query = query.Where(log => log.CorrelationId == correlationId);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(log =>
                    log.Description.Contains(searchTerm) ||
                    log.Details.Contains(searchTerm) ||
                    log.Resource.Contains(searchTerm) ||
                    log.Action.Contains(searchTerm) ||
                    log.ReferenceId.Contains(searchTerm) ||
                    log.ReferenceName.Contains(searchTerm));
            }

            // Get the total count before pagination
            var totalCount = await query.CountAsync();

            // Apply sorting and pagination
            var items = await query
                .ApplySorting(sortBy, sortDirection)
                .ApplyPaging(page, pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        /// <summary>
        /// Gets audit logs by correlation ID
        /// </summary>
        /// <param name="correlationId"> The correlation ID </param>
        /// <returns> A list of audit logs with the specified correlation ID </returns>
        public async Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.CorrelationId == correlationId)
                .OrderByDescending(log => log.Timestamp)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs for a specific resource
        /// </summary>
        /// <param name="resource">    The resource name </param>
        /// <param name="referenceId"> The reference ID </param>
        /// <returns> A list of audit logs for the specified resource </returns>
        public async Task<IEnumerable<AuditLog>> GetByResourceAsync(string resource, string referenceId)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.Resource == resource && log.ReferenceId == referenceId)
                .OrderByDescending(log => log.Timestamp)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs for a specific client
        /// </summary>
        /// <param name="clientId"> The client ID </param>
        /// <param name="limit">    The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified client </returns>
        public async Task<IEnumerable<AuditLog>> GetByClientIdAsync(string clientId, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.ClientId == clientId)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs for a specific user
        /// </summary>
        /// <param name="userId"> The user ID </param>
        /// <param name="limit">  The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified user </returns>
        public async Task<IEnumerable<AuditLog>> GetByUserIdAsync(string userId, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.UserId == userId)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs for a specific IP address
        /// </summary>
        /// <param name="ipAddress"> The IP address </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs for the specified IP address </returns>
        public async Task<IEnumerable<AuditLog>> GetByIpAddressAsync(string ipAddress, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.IpAddress == ipAddress)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs by event type
        /// </summary>
        /// <param name="eventType"> The event type </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified event type </returns>
        public async Task<IEnumerable<AuditLog>> GetByEventTypeAsync(AuditEventType eventType, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.EventType == eventType)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs by status
        /// </summary>
        /// <param name="status"> The status </param>
        /// <param name="limit">  The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified status </returns>
        public async Task<IEnumerable<AuditLog>> GetByStatusAsync(AuditEventStatus status, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.Status == status)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs by severity
        /// </summary>
        /// <param name="severity"> The severity </param>
        /// <param name="limit">    The maximum number of logs to return </param>
        /// <returns> A list of audit logs with the specified severity </returns>
        public async Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditEventSeverity severity, int limit = 100)
        {
            return await _context.Set<AuditLog>()
                .Where(log => log.Severity == severity)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets audit logs by date range
        /// </summary>
        /// <param name="startDate"> The start date </param>
        /// <param name="endDate">   The end date </param>
        /// <param name="limit">     The maximum number of logs to return </param>
        /// <returns> A list of audit logs within the specified date range </returns>
        public async Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int limit = 100)
        {
            // Include the entire end date (up to 23:59:59)
            var endOfDay = endDate.Date.AddDays(1).AddTicks(-1);

            return await _context.Set<AuditLog>()
                .Where(log => log.Timestamp >= startDate && log.Timestamp <= endOfDay)
                .OrderByDescending(log => log.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        public Task<AuditLog> GetByIdAsync(long id) =>
            _context.AuditLogs
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == id);

        /// <summary>
        /// Gets expired audit logs
        /// </summary>
        /// <returns> A list of expired audit logs </returns>
        public async Task<IEnumerable<AuditLog>> GetExpiredAsync()
        {
            var now = DateTime.UtcNow;

            return await _context.Set<AuditLog>()
                .Where(log => log.ExpiresAt.HasValue && log.ExpiresAt.Value < now)
                .ToListAsync();
        }

        /// <summary>
        /// Deletes expired audit logs
        /// </summary>
        /// <returns> The number of deleted logs </returns>
        public async Task<int> DeleteExpiredAsync()
        {
            var now = DateTime.UtcNow;

            var expiredLogs = await _context.Set<AuditLog>()
                .Where(log => log.ExpiresAt.HasValue && log.ExpiresAt.Value < now)
                .ToListAsync();

            if (expiredLogs.Any())
            {
                _context.Set<AuditLog>().RemoveRange(expiredLogs);
                await _context.SaveChangesAsync();
            }

            return expiredLogs.Count;
        }

        /// <summary>
        /// Gets audit log statistics
        /// </summary>
        /// <param name="startDate"> The start date </param>
        /// <param name="endDate">   The end date </param>
        /// <returns> Audit log statistics </returns>
        public async Task<AuditLogStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            // Start with all audit logs
            var query = _context.Set<AuditLog>().AsQueryable();

            // Apply date filters
            if (startDate.HasValue)
            {
                query = query.Where(log => log.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // Include the entire end date (up to 23:59:59)
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(log => log.Timestamp <= endOfDay);
            }

            // Get all logs for statistics
            var logs = await query.ToListAsync();

            // Calculate statistics
            var statistics = new AuditLogStatistics
            {
                TotalCount = logs.Count,
                CountByEventType = logs.GroupBy(log => log.EventType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByCategory = logs.GroupBy(log => log.Category)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountBySeverity = logs.GroupBy(log => log.Severity)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByStatus = logs.GroupBy(log => log.Status)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByResource = logs.GroupBy(log => log.Resource)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByAction = logs.GroupBy(log => log.Action)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByClientId = logs.Where(log => !string.IsNullOrEmpty(log.ClientId))
                    .GroupBy(log => log.ClientId)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByUserId = logs.Where(log => !string.IsNullOrEmpty(log.UserId))
                    .GroupBy(log => log.UserId)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByIpAddress = logs.Where(log => !string.IsNullOrEmpty(log.IpAddress))
                    .GroupBy(log => log.IpAddress)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByDay = logs.GroupBy(log => log.Timestamp.Date)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return statistics;
        }
    }
}