// Purpose:
//   Lightweight liveness check for Kafka consumer readiness. Does NOT produce/consume messages.
//   - Validates feature flag & KafkaOptions configuration
//   - Attempts a short metadata fetch against the broker to ensure connectivity
// Returns:
//   Healthy   → when enabled, config is valid, and metadata returns at least one broker
//   Degraded  → when enabled, config is present but metadata has no brokers
//   Unhealthy → when enabled, but cannot reach brokers or configuration is invalid
// Notes:
//   • Registered in Program.cs only when Features:Kafka:Enabled == true (so this runs only if Kafka is on).
//   • Uses Confluent.Kafka AdminClient for a non-intrusive metadata check.
// =====================================================================================================
namespace Sage200Microservice.API.HealthChecks
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Confluent.Kafka;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Sage200Microservice.Services.Messaging;

    /// <summary>
    /// Liveness health check for Kafka consumers. Verifies configuration and broker reachability
    /// using a short AdminClient metadata request (no produce/consume).
    /// </summary>
    public sealed class KafkaConsumerLivenessHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        private readonly KafkaOptions _kafka;
        private readonly ILogger<KafkaConsumerLivenessHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaConsumerLivenessHealthCheck"/> class.
        /// </summary>
        public KafkaConsumerLivenessHealthCheck(
            IConfiguration configuration,
            IOptions<KafkaOptions> kafkaOptions,
            ILogger<KafkaConsumerLivenessHealthCheck> logger)
        {
            _configuration = configuration;
            _kafka = kafkaOptions.Value;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // The check is registered only when Kafka is enabled, but we double-guard for safety.
                var enabled = _configuration.GetValue<bool>("Features:Kafka:Enabled");
                if (!enabled)
                {
                    return Task.FromResult(HealthCheckResult.Healthy("Kafka feature flag is disabled."));
                }

                if (string.IsNullOrWhiteSpace(_kafka.BootstrapServers))
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("Kafka BootstrapServers is not configured."));
                }

                var adminConfig = new AdminClientConfig
                {
                    BootstrapServers = _kafka.BootstrapServers,
                    ClientId = string.IsNullOrWhiteSpace(_kafka.ClientId) ? "sage200-healthcheck-admin" : _kafka.ClientId
                };

                // Optional security plumbing
                if (!string.IsNullOrWhiteSpace(_kafka.SecurityProtocol) &&
                    Enum.TryParse<SecurityProtocol>(_kafka.SecurityProtocol, true, out var sec))
                {
                    adminConfig.SecurityProtocol = sec;
                }
                if (!string.IsNullOrWhiteSpace(_kafka.SaslMechanism) &&
                    Enum.TryParse<SaslMechanism>(_kafka.SaslMechanism, true, out var mech))
                {
                    adminConfig.SaslMechanism = mech;
                }
                if (!string.IsNullOrWhiteSpace(_kafka.SaslUsername))
                    adminConfig.SaslUsername = _kafka.SaslUsername;
                if (!string.IsNullOrWhiteSpace(_kafka.SaslPassword))
                    adminConfig.SaslPassword = _kafka.SaslPassword;

                using var admin = new AdminClientBuilder(adminConfig).Build();

                // Try a short metadata request to confirm reachability.
                // Note: This is a non-intrusive call; it does not create topics or produce/consume.
                var metadata = admin.GetMetadata(TimeSpan.FromSeconds(3));

                if (metadata is null)
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("Kafka metadata returned null."));
                }

                if (metadata.Brokers is null || metadata.Brokers.Count == 0)
                {
                    return Task.FromResult(HealthCheckResult.Degraded("Kafka metadata contains no brokers."));
                }

                return Task.FromResult(HealthCheckResult.Healthy("Kafka brokers reachable."));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kafka liveness check failed.");
                return Task.FromResult(HealthCheckResult.Unhealthy($"Kafka liveness check exception: {ex.Message}"));
            }
        }
    }
}