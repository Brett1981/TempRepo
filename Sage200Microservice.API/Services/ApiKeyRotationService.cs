using Microsoft.Extensions.Options;
using Sage200Microservice.API.Configuration;
using Sage200Microservice.API.Metrics;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Services
{
    /// <summary>
    /// Background service for automatic API key rotation
    /// </summary>
    public class ApiKeyRotationService : BackgroundService
    {
        private readonly ILogger<ApiKeyRotationService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApiKeyRotationOptions _options;
        private readonly BackgroundServiceMetrics _metrics;

        /// <summary>
        /// Initializes a new instance of the ApiKeyRotationService class
        /// </summary>
        /// <param name="logger">          The logger </param>
        /// <param name="serviceProvider"> The service provider </param>
        /// <param name="options">         The API key rotation options </param>
        /// <param name="metrics">         The background service metrics </param>
        public ApiKeyRotationService(
            ILogger<ApiKeyRotationService> logger,
            IServiceProvider serviceProvider,
            IOptions<ApiKeyRotationOptions> options,
            BackgroundServiceMetrics metrics)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _options = options.Value;
            _metrics = metrics;
        }

        /// <summary>
        /// Executes the background service
        /// </summary>
        /// <param name="stoppingToken"> The cancellation token </param>
        /// <returns> A task representing the asynchronous operation </returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("API Key Rotation Service is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var executionTimer = _metrics.TrackBackgroundServiceExecution("ApiKeyRotationService");

                    _logger.LogInformation("Running API key rotation check");
                    _metrics.RecordBackgroundServiceExecution("ApiKeyRotationService", "started");

                    // Only run if enabled
                    if (_options.Enabled)
                    {
                        await RotateKeysAsync();
                        await CleanupExpiredKeysAsync();
                    }
                    else
                    {
                        _logger.LogInformation("API key rotation is disabled");
                    }

                    _metrics.RecordBackgroundServiceExecution("ApiKeyRotationService", "completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in API key rotation service");
                    _metrics.RecordBackgroundServiceError("ApiKeyRotationService", ex.GetType().Name);
                }

                // Wait for the next interval
                await Task.Delay(TimeSpan.FromHours(_options.CheckIntervalHours), stoppingToken);
            }

            _logger.LogInformation("API Key Rotation Service is stopping");
        }

        /// <summary>
        /// Rotates API keys that are due for rotation
        /// </summary>
        /// <returns> A task representing the asynchronous operation </returns>
        private async Task RotateKeysAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
            var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

            var keysDueForRotation = await apiKeyRepository.GetKeysDueForRotationAsync(_options.MaxKeyAgeInDays);

            _logger.LogInformation("Found {Count} API keys due for rotation", keysDueForRotation.Count);
            _metrics.SetBatchProcessingQueueSize("ApiKeyRotationService", "keys_to_rotate", keysDueForRotation.Count);

            int rotatedCount = 0;
            int failedCount = 0;

            foreach (var key in keysDueForRotation)
            {
                try
                {
                    _logger.LogInformation("Rotating API key for client {ClientName} (ID: {Id})", key.ClientName, key.Id);

                    await apiKeyService.RotateAsync(key.Id, _options.GracePeriodDays);

                    rotatedCount++;
                    _metrics.RecordItemProcessed("ApiKeyRotationService", "api_key", "rotated");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to rotate API key for client {ClientName} (ID: {Id})", key.ClientName, key.Id);
                    failedCount++;
                    _metrics.RecordItemFailedProcessing("ApiKeyRotationService", "api_key", ex.GetType().Name);
                }
            }

            _logger.LogInformation("API key rotation completed: {RotatedCount} rotated, {FailedCount} failed", rotatedCount, failedCount);
        }

        /// <summary>
        /// Cleans up expired previous keys
        /// </summary>
        /// <returns> A task representing the asynchronous operation </returns>
        private async Task CleanupExpiredKeysAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

            try
            {
                var cleanedCount = await apiKeyRepository.CleanupExpiredPreviousKeysAsync();

                _logger.LogInformation("Cleaned up {Count} expired previous keys", cleanedCount);
                _metrics.RecordItemProcessed("ApiKeyRotationService", "expired_keys", "cleaned");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired previous keys");
                _metrics.RecordItemFailedProcessing("ApiKeyRotationService", "expired_keys", ex.GetType().Name);
            }
        }
    }
}