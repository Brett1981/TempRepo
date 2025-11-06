using Microsoft.Extensions.Options;
using Sage200Microservice.API.Metrics;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.API.Services
{
    /// <summary>
    /// Background service for cleaning up expired audit logs
    /// </summary>
    public class AuditLogCleanupService : BackgroundService
    {
        private readonly ILogger<AuditLogCleanupService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly AuditLogSettings _settings;
        private readonly BackgroundServiceMetrics _metrics;

        /// <summary>
        /// Initializes a new instance of the AuditLogCleanupService class
        /// </summary>
        /// <param name="logger">          The logger </param>
        /// <param name="serviceProvider"> The service provider </param>
        /// <param name="options">         The audit log settings </param>
        /// <param name="metrics">         The background service metrics </param>
        public AuditLogCleanupService(
            ILogger<AuditLogCleanupService> logger,
            IServiceProvider serviceProvider,
            IOptions<AuditLogSettings> options,
            BackgroundServiceMetrics metrics)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settings = options.Value;
            _metrics = metrics;
        }

        /// <summary>
        /// Executes the background service
        /// </summary>
        /// <param name="stoppingToken"> The cancellation token </param>
        /// <returns> A task representing the asynchronous operation </returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Audit Log Cleanup Service is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var executionTimer = _metrics.TrackBackgroundServiceExecution("AuditLogCleanupService");

                    _logger.LogInformation("Running audit log cleanup");
                    _metrics.RecordBackgroundServiceExecution("AuditLogCleanupService", "started");

                    // Only run if enabled
                    if (_settings.Enabled && _settings.EnableRetention)
                    {
                        await CleanupExpiredLogsAsync();
                    }
                    else
                    {
                        _logger.LogInformation("Audit log cleanup is disabled");
                    }

                    _metrics.RecordBackgroundServiceExecution("AuditLogCleanupService", "completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in audit log cleanup service");
                    _metrics.RecordBackgroundServiceError("AuditLogCleanupService", ex.GetType().Name);
                }

                // Wait for the next interval
                await Task.Delay(TimeSpan.FromHours(_settings.CleanupIntervalHours), stoppingToken);
            }

            _logger.LogInformation("Audit Log Cleanup Service is stopping");
        }

        /// <summary>
        /// Cleans up expired audit logs
        /// </summary>
        /// <returns> A task representing the asynchronous operation </returns>
        private async Task CleanupExpiredLogsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            try
            {
                var deletedCount = await auditLogService.DeleteExpiredAsync();

                _logger.LogInformation("Deleted {Count} expired audit logs", deletedCount);
                _metrics.RecordItemProcessed("AuditLogCleanupService", "audit_logs", "deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired audit logs");
                _metrics.RecordItemFailedProcessing("AuditLogCleanupService", "audit_logs", ex.GetType().Name);
            }
        }
    }
}