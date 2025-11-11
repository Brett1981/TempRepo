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
    /// Injects Sage routing headers with strict precedence:
    /// 1) Existing outgoing request headers win.
    /// 2) Per-request overrides via HttpContext.Items["X-Site"/"X-Company"].
    /// 3) Inbound HTTP headers from the current request.
    /// 4) Ambient SageCallContext (Kafka/background processing).
    /// 5) DEV-only defaults from configuration (gated by AllowDevelopmentFallbackApiKey).
    ///
    /// IMPORTANT:
    /// - Never forwards the microservice API key to Sage (strips ApiKey header if present).
    /// - Emits X-Routing-Defaults: "site,company" when defaults were applied (observability).
    /// </summary>
    public sealed class SageRoutingHeaderHandler : DelegatingHandler
    {
        private readonly ILogger<SageRoutingHeaderHandler> _log;
        private readonly IHttpContextAccessor _http;
        private readonly IOptions<SageApiSettings> _opt;
        private readonly IHostEnvironment _env;

        public SageRoutingHeaderHandler(
            ILogger<SageRoutingHeaderHandler> log,
            IHttpContextAccessor http,
            IOptions<SageApiSettings> opt,
            IHostEnvironment env)
        {
            _log = log;
            _http = http;
            _opt = opt;
            _env = env;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cfg = _opt.Value;

            // Resolve configurable header names (with sane fallbacks)
            var siteHeaderName   = string.IsNullOrWhiteSpace(cfg.SiteHeaderName)    ? "X-Site"    : cfg.SiteHeaderName!;
            var compHeaderName   = string.IsNullOrWhiteSpace(cfg.CompanyHeaderName) ? "X-Company" : cfg.CompanyHeaderName!;
            var apiKeyHeaderName = string.IsNullOrWhiteSpace(cfg.ApiKeyHeaderName)  ? "X-Api-Key" : cfg.ApiKeyHeaderName!;

            // 1) Already present on the outgoing request?
            bool hasSite = request.Headers.Contains(siteHeaderName);
            bool hasComp = request.Headers.Contains(compHeaderName);

            string? site = null;
            string? comp = null;

            // 2) Per-request overrides via HttpContext.Items (set by controller/middleware)
            var httpCtx = _http.HttpContext;
            if (httpCtx is not null)
            {
                if (!hasSite && TryReadItem(httpCtx, siteHeaderName, out var s)) site = s;
                if (!hasComp && TryReadItem(httpCtx, compHeaderName, out var c)) comp = c;
            }

            // 3) Inbound HTTP headers from the current request
            if (httpCtx is not null)
            {
                if (!hasSite && string.IsNullOrWhiteSpace(site)) site = TryReadHeader(httpCtx, siteHeaderName);
                if (!hasComp && string.IsNullOrWhiteSpace(comp)) comp = TryReadHeader(httpCtx, compHeaderName);
            }

            // 4) Ambient context (Kafka/background workers)
            var ambient = SageCallContext.Current;
            if (!hasSite && string.IsNullOrWhiteSpace(site)) site = ambient?.SiteId;
            if (!hasComp && string.IsNullOrWhiteSpace(comp)) comp = ambient?.CompanyId;

            // 5) DEV-only routing defaults from configuration (explicitly gated)
            // This prevents accidental use of stale defaults with real user tokens.
            string applied = "";
            if (_env.IsDevelopment() && cfg.AllowDevelopmentFallbackApiKey)
            {
                if (!hasSite && string.IsNullOrWhiteSpace(site) && !string.IsNullOrWhiteSpace(cfg.SiteId))
                {
                    site = cfg.SiteId;
                    applied = "site";
                }
                if (!hasComp && string.IsNullOrWhiteSpace(comp) && !string.IsNullOrWhiteSpace(cfg.CompanyId))
                {
                    comp = cfg.CompanyId;
                    applied = string.IsNullOrEmpty(applied) ? "company" : $"{applied},company";
                }
            }

            // Inject resolved routing headers (only if not already present)
            TryAdd(request, siteHeaderName, site, ref hasSite);
            TryAdd(request, compHeaderName, comp, ref hasComp);

            // Never forward the microservice API key to Sage
            if (request.Headers.Contains(apiKeyHeaderName))
                request.Headers.Remove(apiKeyHeaderName);

            // Diagnostics
            if (hasSite && hasComp)
            {
                _log.LogDebug("Routing headers set: {SiteHeader}={Site} {CompHeader}={Company}",
                    siteHeaderName, TryGet(request, siteHeaderName),
                    compHeaderName, TryGet(request, compHeaderName));
            }

            if (!string.IsNullOrWhiteSpace(applied))
            {
                request.Headers.Remove("X-Routing-Defaults");
                request.Headers.TryAddWithoutValidation("X-Routing-Defaults", applied);
                _log.LogInformation("Routing defaults applied: {Applied} (corr={CorrelationId})",
                    applied, TryGet(request, "X-Correlation-Id"));
            }

            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static bool TryReadItem(HttpContext ctx, string key, out string? value)
        {
            if (ctx.Items.TryGetValue(key, out var obj) && obj is string s && !string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
            value = null;
            return false;
        }

        private static string? TryReadHeader(HttpContext ctx, string name) =>
            ctx.Request?.Headers.TryGetValue(name, out var v) == true ? v.ToString() : null;

        private static void TryAdd(HttpRequestMessage req, string name, string? value, ref bool alreadyPresent)
        {
            if (alreadyPresent || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) return;
            req.Headers.TryAddWithoutValidation(name, value);
            alreadyPresent = true;
        }

        private static string? TryGet(HttpRequestMessage req, string name) =>
            req.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
    }
}
