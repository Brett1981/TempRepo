using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Infrastructure;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Injects required Sage routing headers (X-Site, X-Company, X-Api-Key) with strict precedence:
    /// 1) Existing request headers (already present) win.
    /// 2) Ambient Kafka/worker context via SageCallContext (if present).
    /// 3) Defaults from appsettings (SageApi:SiteId/CompanyId).
    /// Additionally, in Development: inject DevelopmentDefaultApiKey if X-Api-Key is missing and allowed.
    /// Emits X-Routing-Defaults to indicate which defaults were applied: "site,company,apiKey".
    /// </summary>
    public sealed class SageRoutingHeaderHandler : DelegatingHandler
    {
        private readonly ILogger<SageRoutingHeaderHandler> _log;
        private readonly IHttpContextAccessor _http;
        private readonly SageApiSettings _cfg;
        private readonly IHostEnvironment _env;

        public SageRoutingHeaderHandler(
            ILogger<SageRoutingHeaderHandler> log,
            IHttpContextAccessor http,
            IOptions<SageApiSettings> cfg,
            IHostEnvironment env)
        {
            _log = log;
            _http = http;
            _cfg = cfg.Value;
            _env = env;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Resolve names once (configurable)
            var siteHeader = _cfg.SiteHeaderName ?? "X-Site";
            var compHeader = _cfg.CompanyHeaderName ?? "X-Company";
            var apiKeyHeader = _cfg.ApiKeyHeaderName ?? "X-Api-Key";

            // 1) Existing headers?
            bool hasSite = request.Headers.Contains(siteHeader);
            bool hasComp = request.Headers.Contains(compHeader);
            bool hasApiKey = request.Headers.Contains(apiKeyHeader);

            // 2) Ambient (Kafka/Background)
            var ambient = SageCallContext.Current; // could be null
            string? site = hasSite ? null : ambient?.SiteId;
            string? comp = hasComp ? null : ambient?.CompanyId;
            string? apiKey = hasApiKey ? null : ambient?.ApiKey;

            // 3) HTTP context
            var ctx = _http.HttpContext;
            if (ctx is not null)
            {
                if (!hasSite && string.IsNullOrWhiteSpace(site))
                    site = HeaderValue(ctx, siteHeader);
                if (!hasComp && string.IsNullOrWhiteSpace(comp))
                    comp = HeaderValue(ctx, compHeader);
                if (!hasApiKey && string.IsNullOrWhiteSpace(apiKey))
                    apiKey = HeaderValue(ctx, apiKeyHeader);
            }

            // 4) Defaults from configuration
            string applied = "";
            if (!hasSite && string.IsNullOrWhiteSpace(site))
            {
                site = _cfg.SiteId; applied += "site";
            }
            if (!hasComp && string.IsNullOrWhiteSpace(comp))
            {
                comp = _cfg.CompanyId; applied += (applied.Length > 0 ? ",company" : "company");
            }

            // Dev default API key (only when allowed)
            if (!hasApiKey && string.IsNullOrWhiteSpace(apiKey) && _env.IsDevelopment() && _cfg.AllowDevelopmentFallbackApiKey)
            {
                apiKey = _cfg.DevelopmentDefaultApiKey;
                applied += (applied.Length > 0 ? ",apiKey" : "apiKey");
            }

            // Inject if we resolved values
            TryAdd(request, siteHeader, site, ref hasSite);
            TryAdd(request, compHeader, comp, ref hasComp);
            TryAdd(request, apiKeyHeader, apiKey, ref hasApiKey);

            // Emit a sentinel when defaults were applied (for precise observability)
            if (!string.IsNullOrWhiteSpace(applied))
            {
                request.Headers.Remove("X-Routing-Defaults");
                request.Headers.TryAddWithoutValidation("X-Routing-Defaults", applied);
                _log.LogInformation("Routing defaults applied: {Applied} (corr={CorrelationId})",
                    applied, TryGet(request, "X-Correlation-Id"));
            }

            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static string? HeaderValue(HttpContext ctx, string name)
            => ctx.Request?.Headers.TryGetValue(name, out var v) == true ? v.ToString() : null;

        private static void TryAdd(HttpRequestMessage req, string name, string? value, ref bool alreadyPresent)
        {
            if (alreadyPresent || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) return;
            req.Headers.TryAddWithoutValidation(name, value);
            alreadyPresent = true;
        }

        private static string? TryGet(HttpRequestMessage req, string name)
            => req.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
    }
}
