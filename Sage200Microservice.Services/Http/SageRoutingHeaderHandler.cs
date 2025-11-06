using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Configuration;
using Sage200Microservice.Services.Infrastructure;
using Sage200Microservice.Services.Models;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Ensures all outbound Sage requests include required headers:
    /// X-Site, X-Company (always), and X-Api-Key (always; Dev fallback allowed).
    /// Header source precedence:
    /// 1) Inbound HTTP request headers (if HttpContext exists),
    /// 2) Ambient SageCallContext (Kafka/scheduled flows),
    /// 3) appsettings defaults (for Site/Company), and Dev default API key if allowed.
    /// Logs whenever defaults are applied to aid observability.
    /// </summary>
    public sealed class SageRoutingHeaderHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SageApiSettings _settings;
        private readonly IHostEnvironment _env;
        private readonly ILogger<SageRoutingHeaderHandler> _logger;

        public SageRoutingHeaderHandler(
            IHttpContextAccessor httpContextAccessor,
            IOptions<SageApiSettings> settings,
            IHostEnvironment env,
            ILogger<SageRoutingHeaderHandler> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _settings = settings.Value;
            _env = env;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 1) Try inbound HTTP headers if available.
            var ctx = _httpContextAccessor.HttpContext;
            string? site = null, company = null, apiKey = null;

            if (ctx is not null)
            {
                var reqHeaders = ctx.Request.Headers;
                if (reqHeaders.TryGetValue(_settings.SiteHeaderName, out var s)) site = s.ToString();
                if (reqHeaders.TryGetValue(_settings.CompanyHeaderName, out var c)) company = c.ToString();
                if (reqHeaders.TryGetValue(_settings.ApiKeyHeaderName, out var k)) apiKey = k.ToString();
            }

            // 2) If missing, try ambient Kafka/scheduled context.
            var ambient = SageCallContext.Current;
            site ??= ambient?.SiteId;
            company ??= ambient?.CompanyId;
            apiKey ??= ambient?.ApiKey;

            // 3) Fill fallbacks (Site/Company ALWAYS; ApiKey only in Dev when allowed).
            bool siteDefaulted = false, companyDefaulted = false, apiKeyDefaulted = false;

            if (string.IsNullOrWhiteSpace(site))
            {
                site = _settings.SiteId;
                siteDefaulted = true;
            }

            if (string.IsNullOrWhiteSpace(company))
            {
                company = _settings.CompanyId;
                companyDefaulted = true;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (_env.IsDevelopment() && _settings.AllowDevelopmentFallbackApiKey && !string.IsNullOrWhiteSpace(_settings.DevelopmentDefaultApiKey))
                {
                    apiKey = _settings.DevelopmentDefaultApiKey;
                    apiKeyDefaulted = true;
                }
            }

            // Apply headers to outbound request.
            // (Do not overwrite if caller already added them manually to the HttpRequestMessage.)
            if (!request.Headers.Contains(_settings.SiteHeaderName) && !string.IsNullOrWhiteSpace(site))
                request.Headers.TryAddWithoutValidation(_settings.SiteHeaderName, site);

            if (!request.Headers.Contains(_settings.CompanyHeaderName) && !string.IsNullOrWhiteSpace(company))
                request.Headers.TryAddWithoutValidation(_settings.CompanyHeaderName, company);

            if (!request.Headers.Contains(_settings.ApiKeyHeaderName) && !string.IsNullOrWhiteSpace(apiKey))
                request.Headers.TryAddWithoutValidation(_settings.ApiKeyHeaderName, apiKey);

            // Observability: log precisely when defaults were applied.
            if (siteDefaulted || companyDefaulted || apiKeyDefaulted)
            {
                _logger.LogInformation(
                    "Sage header defaults applied (Site:{SiteDefaulted}, Company:{CompanyDefaulted}, ApiKey:{ApiKeyDefaulted}) for {Method} {Uri}",
                    siteDefaulted, companyDefaulted, apiKeyDefaulted, request.Method, request.RequestUri
                );
            }

            // If API key is still missing in non-Dev, we let upstream enforcement/middleware reject the call.
            return base.SendAsync(request, cancellationToken);
        }
    }
}
