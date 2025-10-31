using Microsoft.Extensions.Options;
using Sage200Microservice.API.Configuration;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Middleware for API key authentication with a built-in allowance for the
    /// Business Dashboard. For dashboard routes, if X-Api-Key is missing we
    /// automatically inject the service default key (ID=3).
    /// </summary>
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

        private const string ApiKeyHeaderName = "X-Api-Key";

        // Your non-expiring service key (ID = 3)
        private const string ServiceDefaultApiKey = "lJ9CvaBZyV3dWYYPeUKpqlvFV2AWOvpm7Daaat9nxYU";

        public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IApiKeyRepository apiKeyRepository, IOptions<ServiceApiKeyOptions> svcKeyOpt)
        {
            // 1) Skip for swagger/health (as before)
            if (IsSwaggerRequest(context) || IsHealthCheckRequest(context))
            {
                await _next(context);
                return;
            }

            // 2) Dashboard allow-list: static page + its data APIs
            bool isDashboard = context.Request.Path.StartsWithSegments("/business-dashboard")
                            || context.Request.Path.StartsWithSegments("/business-dashboard.html")
                            || context.Request.Path.StartsWithSegments("/api/business-metrics")
                            || context.Request.Path.StartsWithSegments("/api/admin/links")
                            || context.Request.Path.StartsWithSegments("/api/metrics");

            // If dashboard call and no key provided -> inject the service default key
            if (isDashboard && !context.Request.Headers.TryGetValue(ApiKeyHeaderName, out _))
            {
                var svcKey = svcKeyOpt.Value.DefaultKey;
                context.Request.Headers[ApiKeyHeaderName] = svcKey;
            }

            // From here on, behave exactly as before: require a key
            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                _logger.LogWarning("API key was not provided. Request from {IpAddress}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "API key is missing" });
                return;
            }

            var apiKey = extractedApiKey.ToString();

            if (!await apiKeyRepository.IsValidKeyAsync(apiKey))
            {
                _logger.LogWarning("Invalid API key provided: {ApiKey}. Request from {IpAddress}",
                    apiKey, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid API key" });
                return;
            }

            await apiKeyRepository.UpdateLastUsedAsync(apiKey);

            var apiKeyEntity = await apiKeyRepository.GetByKeyAsync(apiKey);
            context.Items["ClientName"] = apiKeyEntity?.ClientName ?? apiKey;

            await _next(context);
        }


        private static bool IsSwaggerRequest(HttpContext context) =>
            context.Request.Path.StartsWithSegments("/swagger")
            || context.Request.Path.StartsWithSegments("/index.html")
            || context.Request.Path.StartsWithSegments("/openapi");

        private static bool IsHealthCheckRequest(HttpContext context) =>
            context.Request.Path.StartsWithSegments("/health")
            || context.Request.Path.StartsWithSegments("/api/health");

        /// <summary>
        /// Identifies the Business Dashboard HTML page and assets, and its backing data endpoints.
        /// Adjust the paths here if your controller route or filenames differ.
        /// </summary>
        private static bool IsDashboardOrMetricsRequest(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            // Static dashboard assets
            if (path.StartsWith("/business-dashboard")
                || path.StartsWith("/business-dashboard.html")
                || path.StartsWith("/dashboardscript.js")
                || path.StartsWith("/business-dashboard/"))
            {
                return true;
            }

            // Backing REST endpoints feeding the dashboard
            // (keep both singular/plural variants to match your controller route)
            if (path.StartsWith("/api/business-metrics")
                || path.StartsWith("/api/businessmetrics"))
            {
                return true;
            }

            // Prometheus scrape (optional)
            if (path.StartsWith("/metrics"))
                return true;

            return false;
        }
    }
}
