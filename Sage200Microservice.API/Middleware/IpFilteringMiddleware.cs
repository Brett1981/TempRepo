using Microsoft.Extensions.Options;
using Sage200Microservice.API.Configuration;
using Sage200Microservice.API.Security;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Middleware for IP address filtering (IMiddleware pattern). Safe for injecting scoped
    /// services like IApiKeyRepository.
    /// </summary>
    public sealed class IpFilteringMiddleware : IMiddleware
    {
        private readonly ILogger<IpFilteringMiddleware> _logger;
        private readonly IpFilteringOptions _options;
        private readonly IApiKeyRepository _apiKeyRepository;

        public IpFilteringMiddleware(
            ILogger<IpFilteringMiddleware> logger,
            IOptions<IpFilteringOptions> options,
            IApiKeyRepository apiKeyRepository)
        {
            _logger = logger;
            _options = options.Value;
            _apiKeyRepository = apiKeyRepository;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // Disabled? Skip.
            if (!_options.Enabled)
            {
                await next(context);
                return;
            }

            // Exclusions
            var path = context.Request.Path.Value ?? string.Empty;
            if (_options.ExcludedEndpoints.Any(e =>
                    path.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                await next(context);
                return;
            }

            // Determine client IP
            var remoteIp = context.Connection.RemoteIpAddress;
            var forwardedFor = context.Request.Headers[_options.RealIpHeader].ToString();
            var clientIp = IpAddressHelper.GetClientIpAddress(
                remoteIp, forwardedFor, _options.TrustXForwardedFor);

            if (clientIp is null)
            {
                _logger.LogWarning("IP filter: failed to determine client IP. Blocking request.");
                context.Response.StatusCode = _options.HttpStatusCode;
                return;
            }

            // Client-specific restrictions via header + config/db
            var clientId = context.Request.Headers[_options.ClientIdHeader].ToString();

            // 1) Config-based client restrictions
            if (!string.IsNullOrEmpty(clientId)
                && _options.ClientRestrictions.TryGetValue(clientId, out var restr))
            {
                if (restr.AllowedIps.Count > 0 &&
                    !IpAddressHelper.IsInRanges(clientIp, restr.AllowedIps))
                {
                    _logger.LogWarning("IP filter: client {ClientId} with IP {Ip} is not in allowed list (config).",
                        clientId, clientIp);
                    context.Response.StatusCode = _options.HttpStatusCode;
                    return;
                }
            }
            // 2) DB-based allowed IPs for API key
            else if (!string.IsNullOrEmpty(clientId))
            {
                var apiKey = await _apiKeyRepository.GetByKeyAsync(clientId);
                if (apiKey != null && !string.IsNullOrWhiteSpace(apiKey.AllowedIpAddresses))
                {
                    var allowed = apiKey.AllowedIpAddresses
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();

                    if (allowed.Count > 0 && !IpAddressHelper.IsInRanges(clientIp, allowed))
                    {
                        _logger.LogWarning("IP filter: client {ClientId} with IP {Ip} not allowed (db).",
                            clientId, clientIp);
                        context.Response.StatusCode = _options.HttpStatusCode;
                        return;
                    }
                }
            }

            // Global policy
            if (_options.DefaultPolicy == IpFilterPolicy.Allow)
            {
                // Allow unless explicitly denied
                if (_options.DeniedIps.Count > 0 &&
                    IpAddressHelper.IsInRanges(clientIp, _options.DeniedIps))
                {
                    _logger.LogWarning("IP filter: IP {Ip} denied by global deny list.", clientIp);
                    context.Response.StatusCode = _options.HttpStatusCode;
                    return;
                }
            }
            else
            {
                // Deny unless explicitly allowed
                if (_options.AllowedIps.Count == 0 ||
                    !IpAddressHelper.IsInRanges(clientIp, _options.AllowedIps))
                {
                    _logger.LogWarning("IP filter: IP {Ip} not in global allowed list.", clientIp);
                    context.Response.StatusCode = _options.HttpStatusCode;
                    return;
                }
            }

            await next(context);
        }
    }

    /// <summary>
    /// Extensions to register and use the IP filtering middleware.
    /// </summary>
    public static class IpFilteringMiddlewareExtensions
    {
        /// <summary>
        /// Registers options and middleware (as scoped) for IP filtering.
        /// </summary>
        public static IServiceCollection AddIpFiltering(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<IpFilteringOptions>(configuration.GetSection("IpFiltering"));

            // IMPORTANT: IMiddleware pattern -> register the middleware type
            services.AddScoped<IpFilteringMiddleware>();

            return services;
        }

        /// <summary>
        /// Adds the IP filtering middleware to the pipeline.
        /// </summary>
        public static IApplicationBuilder UseIpFiltering(this IApplicationBuilder app)
        {
            return app.UseMiddleware<IpFilteringMiddleware>();
        }
    }
}