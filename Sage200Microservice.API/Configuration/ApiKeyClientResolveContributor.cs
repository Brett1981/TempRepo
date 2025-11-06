using AspNetCoreRateLimit;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Resolves the rate-limit client id from a validated X-Api-Key header. Safe for singleton
    /// lifetime (uses IServiceScopeFactory to get scoped services).
    /// </summary>
    public sealed class ApiKeyClientResolveContributor : IClientResolveContributor
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApiKeyClientResolveContributor> _logger;
        private readonly ClientRateLimitOptions _options;

        public ApiKeyClientResolveContributor(
            IServiceScopeFactory scopeFactory,
            IOptions<ClientRateLimitOptions> options,
            ILogger<ApiKeyClientResolveContributor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options?.Value ?? new ClientRateLimitOptions();
        }

        public async Task<string> ResolveClientAsync(HttpContext httpContext)
        {
            // Header name from options, fallback to X-Api-Key
            var headerName = string.IsNullOrWhiteSpace(_options.ClientIdHeader)
                ? "X-Api-Key"
                : _options.ClientIdHeader;

            if (!httpContext.Request.Headers.TryGetValue(headerName, out var values))
                return string.Empty;

            var apiKey = values.ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
                return string.Empty;

            try
            {
                // Create a scope to resolve scoped services safely from a singleton
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

                // Validate key (supports rotation)
                if (!await repo.IsValidKeyAsync(apiKey))
                    return string.Empty;

                var entity =
                    await repo.GetByKeyAsync(apiKey)
                    ?? await repo.GetByPreviousKeyAsync(apiKey);

                var clientId = !string.IsNullOrWhiteSpace(entity?.ClientName)
                    ? entity!.ClientName
                    : apiKey;

                // Stash for downstream diagnostics
                httpContext.Items["RateLimitClientId"] = clientId;

                return clientId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve rate-limit client id from API key.");
                return string.Empty;
            }
        }
    }
}