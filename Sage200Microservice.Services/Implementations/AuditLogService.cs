using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Shared; // Assuming OData helper is here
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Implementations
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ILogger<AuditLogService> _logger;
        private readonly IAuditLogRepository _repo;
        private readonly AuditLogSettings _settings;

        public AuditLogService(
            ILogger<AuditLogService> logger,
            IAuditLogRepository repo,
            IOptions<AuditLogSettings> settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _settings = settings?.Value ?? new AuditLogSettings();
        }

        // ---- helpers --------------------------------------------------------

        private bool AllowedByList(AuditEventType type)
        {
            var list = _settings.EventTypesToLog;
            return list == null || list.Count == 0 || list.Contains(type);
        }

        private bool AllowedBySwitch(AuditEventType type) => type switch
        {
            AuditEventType.Authentication => _settings.LogAuthenticationEvents,
            AuditEventType.Authorization => _settings.LogAuthorizationEvents,
            AuditEventType.DataAccess => _settings.LogDataAccessEvents,
            AuditEventType.DataModification => _settings.LogDataModificationEvents,
            AuditEventType.ApiKeyManagement => _settings.LogApiKeyManagementEvents,
            AuditEventType.System => _settings.LogSystemEvents,
            AuditEventType.ConfigurationChange => _settings.LogSystemEvents,
            AuditEventType.Error => _settings.LogErrorEvents,
            _ => true
        };

        private bool ShouldLog(AuditEventType type)
        {
            if (!_settings.Enabled) return false;
            if (!AllowedBySwitch(type)) return false;
            return AllowedByList(type);
        }

        private async Task<AuditLog> SaveAsync(AuditLog log)
        {
            try
            {
                // retention
                if (_settings.EnableRetention)
                {
                    if (log.RetentionDays <= 0 && _settings.DefaultRetentionDays > 0)
                        log.RetentionDays = _settings.DefaultRetentionDays;

                    if (log.RetentionDays > 0 && !log.ExpiresAt.HasValue)
                        log.ExpiresAt = DateTime.UtcNow.AddDays(log.RetentionDays);
                }

                // trim details if needed
                if (!string.IsNullOrEmpty(log.Details) && _settings.MaxDetailsSize > 0)
                {
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(log.Details);
                    if (bytes > _settings.MaxDetailsSize)
                    {
                        _logger.LogWarning("Audit details exceeded {Max} bytes; truncating.", _settings.MaxDetailsSize);
                        // Simple truncation, adjust if more sophisticated handling is needed
                        log.Details = log.Details.Substring(0, Math.Min(log.Details.Length, (int)(_settings.MaxDetailsSize / 2))); // Estimate chars
                    }
                }

                return await _repo.AddAsync(log); // Assuming AddAsync handles CT implicitly via DbContext
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist audit log.");
                return null; // Don't let logging failures break the application flow
            }
        }

        private static AuditEventSeverity SeverityForStatus(AuditEventStatus status, AuditEventSeverity @default = AuditEventSeverity.Info)
            => (status == AuditEventStatus.Failure || status == AuditEventStatus.Denied) ? AuditEventSeverity.Warning : @default;

        // ---- writes ---------------------------------------------------------

        public async Task<AuditLog> LogAuthenticationEventAsync(
            string userId, string clientId, string ipAddress, string action,
            AuditEventStatus status, string description, object details = null, string correlationId = null)
        {
            var type = AuditEventType.Authentication;
            if (!ShouldLog(type)) return null;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.Security,
                Severity = SeverityForStatus(status),
                Status = status,
                Resource = "Authentication",
                Action = action,
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
            log.SetDetails(details);
            return await SaveAsync(log);
        }

        public async Task<AuditLog> LogAuthorizationEventAsync(
            string userId, string clientId, string ipAddress, string resource, string action,
            AuditEventStatus status, string description, object details = null, string correlationId = null)
        {
            var type = AuditEventType.Authorization;
            if (!ShouldLog(type)) return null;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.Security,
                Severity = SeverityForStatus(status),
                Status = status,
                Resource = resource,
                Action = action,
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
            log.SetDetails(details);
            return await SaveAsync(log);
        }

        // *** Ensured Signature Matches Interface ***
        public async Task<AuditLog> LogDataAccessEventAsync(
            string userId, string clientId, string ipAddress, string resource, string referenceId, string referenceName,
            string action, AuditEventStatus status, string description, object details = null, string correlationId = null,
            CancellationToken cancellationToken = default) // Added CancellationToken to match interface
        {
            var type = AuditEventType.DataAccess;
            if (!ShouldLog(type)) return null;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.Data,
                Severity = SeverityForStatus(status),
                Status = status,
                Resource = resource,
                ReferenceId = referenceId,
                ReferenceName = referenceName,
                Action = action,
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
            log.SetDetails(details);
            // Pass cancellationToken if SaveAsync supports it, otherwise ignore.
            // Assuming SaveAsync uses the DbContext which respects cancellation.
            // If _repo.AddAsync specifically needs the token, pass it: await _repo.AddAsync(log, cancellationToken);
            return await SaveAsync(log);
        }

        // *** Ensured Signature Matches Interface ***
        public async Task<AuditLog> LogDataModificationEventAsync(
            string userId, string clientId, string ipAddress, string resource, string referenceId, string referenceName,
            string action, AuditEventStatus status, string description, object previousState = null,
            object newState = null, object details = null, string correlationId = null,
            CancellationToken cancellationToken = default) // Added CancellationToken to match interface
        {
            var type = AuditEventType.DataModification;
            if (!ShouldLog(type))
                return null;

            var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId;
            var normalizedClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.Business,
                Severity = SeverityForStatus(status),
                Status = status,
                Resource = resource,
                ReferenceId = referenceId,
                ReferenceName = referenceName,
                Action = action,
                Description = description,
                UserId = normalizedUserId,
                ClientId = normalizedClientId,
                IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "0.0.0.0" : ipAddress,
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId,
                PreviousState = ODataToJson(previousState),
                NewState = ODataToJson(newState),
                HttpMethod = "Unknown", // Can be overridden if context is passed
                UrlPath = "Unknown",    // Can be overridden
                UserAgent = "Unknown",  // Can be overridden
                RetentionDays = _settings.DefaultRetentionDays > 0 ? _settings.DefaultRetentionDays : 0
            };

            log.SetDetails(details);
            if (string.IsNullOrEmpty(log.Details)) log.Details = "{}";


            try
            {
                // Pass cancellationToken if SaveAsync supports it, otherwise ignore.
                // If _repo.AddAsync specifically needs the token, pass it: await _repo.AddAsync(log, cancellationToken);
                return await SaveAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log (DataModification).");
                return log; // Return object even if save failed
            }
        }

        public async Task<AuditLog> LogApiKeyManagementEventAsync(
            string userId, string clientId, string ipAddress, string action,
            AuditEventStatus status, string description, object details = null, string correlationId = null)
        {
            var type = AuditEventType.ApiKeyManagement;
            if (!ShouldLog(type)) return null;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.System,
                Severity = SeverityForStatus(status),
                Status = status,
                Resource = "ApiKey",
                Action = action,
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
            log.SetDetails(details);
            return await SaveAsync(log);
        }

        public async Task<AuditLog> LogSystemEventAsync(
            string resource, string action, AuditEventStatus status, string description,
            object details = null, string correlationId = null, AuditEventSeverity severity = AuditEventSeverity.Info)
        {
            var type = AuditEventType.System;
            if (!ShouldLog(type)) return null;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.System,
                Severity = severity,
                Status = status,
                Resource = resource,
                Action = action,
                Description = description,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString()
            };
            log.SetDetails(details);
            return await SaveAsync(log);
        }

        public async Task<AuditLog> LogErrorEventAsync(
            string userId, string clientId, string ipAddress, string resource, string action,
            string description, Exception exception, object details = null, string correlationId = null,
            AuditEventSeverity severity = AuditEventSeverity.Error)
        {
            var type = AuditEventType.Error;
            if (!ShouldLog(type)) return null;

            var payload = new
            {
                error = exception?.Message,
                stack = exception?.StackTrace,
                extra = details
            };

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.System,
                Severity = severity,
                Status = AuditEventStatus.Failure,
                Resource = resource,
                Action = action,
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                Details = ODataToJson(payload) ?? "{}"
            };
            return await SaveAsync(log);
        }


        public async Task<AuditLog> LogHttpRequestAsync(
            string userId, string clientId, string ipAddress, string httpMethod, string urlPath,
            int statusCode, long durationMs, string userAgent, string description,
            object details = null, string correlationId = null)
        {
            var type = AuditEventType.System;
            if (!ShouldLog(type) || !_settings.LogHttpRequests) return null;

            var status = (statusCode >= 200 && statusCode < 400) ? AuditEventStatus.Success : AuditEventStatus.Failure;

            var log = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = type,
                Category = AuditEventCategory.System,
                Severity = SeverityForStatus(status, statusCode >= 500 ? AuditEventSeverity.Error : AuditEventSeverity.Info),
                Status = status,
                Resource = "HTTP",
                Action = "Request",
                Description = description,
                UserId = userId,
                ClientId = clientId,
                IpAddress = ipAddress,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                HttpMethod = httpMethod,
                UrlPath = urlPath,
                HttpStatusCode = statusCode,
                DurationMs = durationMs,
                UserAgent = userAgent
            };
            log.SetDetails(details);
            return await SaveAsync(log);
        }

        // ---- reads / maintenance --------------------------------------------

        public Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId)
            => _repo.GetByCorrelationIdAsync(correlationId);

        public Task<IEnumerable<AuditLog>> GetByResourceAsync(string resource, string referenceId)
            => _repo.GetByResourceAsync(resource, referenceId);

        public Task<(IEnumerable<AuditLog> Items, int TotalCount)> GetFilteredPagedAsync(
            DateTime? startDate = null, DateTime? endDate = null,
            IEnumerable<AuditEventType> eventTypes = null,
            IEnumerable<AuditEventCategory> categories = null,
            IEnumerable<AuditEventSeverity> severities = null,
            IEnumerable<AuditEventStatus> statuses = null,
            string userId = null, string clientId = null, string ipAddress = null,
            string resource = null, string action = null, string correlationId = null,
            string searchTerm = null, int page = 1, int pageSize = 10,
            string sortBy = "Timestamp", string sortDirection = "desc")
            => _repo.GetFilteredPagedAsync(startDate, endDate, eventTypes, categories, severities, statuses,
                                           userId, clientId, ipAddress, resource, action, correlationId,
                                           searchTerm, page, pageSize, sortBy, sortDirection);

        public Task<AuditLogStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
            => _repo.GetStatisticsAsync(startDate, endDate);

        public Task<int> DeleteExpiredAsync()
        {
            if (!_settings.EnableRetention)
            {
                _logger.LogInformation("Audit log retention is disabled. Skipping deletion.");
                return Task.FromResult(0);
            }
            // Add cancellation token if repo method supports it
            return _repo.DeleteExpiredAsync();
        }

        public Task<AuditLog> GetByIdAsync(long id)
        {
            // Ensure ID conversion if necessary, repo might expect int
            // return _repo.GetByIdAsync((int)id); // If repo expects int
            return _repo.GetByIdAsync(id); // Assuming repo expects long
        }

        // Helper to serialize objects to JSON, returning "{}" for null
        // Ensure this helper exists or is accessible, maybe move to a shared utility class
        private static string ODataToJson(object obj)
        {
            if (obj == null) return "{}";
            try
            {
                return JsonSerializer.Serialize(obj, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
            }
            catch (Exception ex)
            {
                // Log serialization error? Or return a structured error JSON
                Console.Error.WriteLine($"AuditLog ODataToJson Error: {ex.Message}"); // Temporary logging
                return $"{{\"serializationError\":\"{ex.Message}\"}}";
            }
        }
    }
}

