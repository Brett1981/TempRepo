using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories; // for AuditLogStatistics

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service interface for audit logging
    /// </summary>
    public interface IAuditLogService
    {
        // ---- Write APIs (return the created log) ----

        Task<AuditLog> LogAuthenticationEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string action,
            AuditEventStatus status,
            string description,
            object details = null,
            string correlationId = null);

        Task<AuditLog> LogAuthorizationEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string resource,
            string action,
            AuditEventStatus status,
            string description,
            object details = null,
            string correlationId = null);

        Task<AuditLog> LogDataAccessEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string resource,
            string referenceId,
            string referenceName,
            string action,
            AuditEventStatus status,
            string description,
            object details = null,
            string correlationId = null,
            CancellationToken cancellationToken = default);

        Task<AuditLog> LogDataModificationEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string resource,
            string referenceId,
            string referenceName,
            string action,
            AuditEventStatus status,
            string description,
            object previousState = null,
            object newState = null,
            object details = null,
            string correlationId = null,
            CancellationToken cancellationToken = default);

        Task<AuditLog> LogApiKeyManagementEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string action,
            AuditEventStatus status,
            string description,
            object details = null,
            string correlationId = null);

        Task<AuditLog> LogSystemEventAsync(
            string resource,
            string action,
            AuditEventStatus status,
            string description,
            object details = null,
            string correlationId = null,
            AuditEventSeverity severity = AuditEventSeverity.Info);

        Task<AuditLog> LogErrorEventAsync(
            string userId,
            string clientId,
            string ipAddress,
            string resource,
            string action,
            string description,
            Exception exception,
            object details = null,
            string correlationId = null,
            AuditEventSeverity severity = AuditEventSeverity.Error);

        Task<AuditLog> LogHttpRequestAsync(
            string userId,
            string clientId,
            string ipAddress,
            string httpMethod,
            string urlPath,
            int statusCode,
            long durationMs,
            string userAgent,
            string description,
            object details = null,
            string correlationId = null);

        // ---- Read/Query APIs ----

        Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId);

        Task<IEnumerable<AuditLog>> GetByResourceAsync(string resource, string referenceId);

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

        Task<AuditLogStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

        Task<int> DeleteExpiredAsync();

        /// <summary>
        /// Gets a single audit log by its ID.
        /// </summary>
        Task<AuditLog> GetByIdAsync(long id);
    }
}