using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.API.HealthChecks
{
    public class SageApiHealthCheck : IHealthCheck
    {
        private readonly ISageAuthenticationService _auth;
        private readonly ILogger<SageApiHealthCheck> _logger;
        private readonly IHostEnvironment _env;
        private readonly IOptions<SageApiSettings> _settings;

        public SageApiHealthCheck(
            ISageAuthenticationService auth,
            ILogger<SageApiHealthCheck> logger,
            IHostEnvironment env,
            IOptions<SageApiSettings> settings)
        {
            _auth = auth;
            _logger = logger;
            _env = env;
            _settings = settings;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Checking Sage API health");

            try
            {
                // Try to obtain a token (fast fail in dev when host doesn't exist).
                var token = await _auth.GetAccessTokenAsync();
                var data = new Dictionary<string, object>
                {
                    ["tokenAcquiredAt"] = DateTime.UtcNow
                };

                return HealthCheckResult.Healthy("Sage API reachable and token acquired.", data);
            }
            catch (Exception ex)
            {
                var data = new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["endpoint"] = _settings.Value?.TokenEndpoint ?? "(unset)"
                };

                // In Development, *degrade* instead of *fail* so /health returns 200 and the app
                // continues to load Swagger/GRPC/UI, while the dashboard still shows the problem clearly.
                if (_env.IsDevelopment())
                {
                    _logger.LogWarning(ex, "Sage API health check degraded in Development: {Message}", ex.Message);
                    return new HealthCheckResult(HealthStatus.Degraded,
                        $"Sage API health check degraded (dev): {ex.Message}", exception: null, data);
                }

                _logger.LogError(ex, "Sage API health check failed");
                return HealthCheckResult.Unhealthy($"Sage API health check failed: {ex.Message}", ex, data);
            }
        }
    }
}