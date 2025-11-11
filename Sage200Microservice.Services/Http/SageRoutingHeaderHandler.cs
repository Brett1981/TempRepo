using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Infrastructure;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Injects required Sage routing headers (X-Site, X-Company) with strict precedence:
    /// 1) Existing request headers (already present) win.
    /// 2) Ambient Kafka/worker context via SageCallContext (if present).
    /// 3) HTTP context headers (if present).
    /// 4) Defaults from appsettings (Sage:SiteId / Sage:CompanyId).
    ///
    /// IMPORTANT: Does NOT forward the microservice API key to Sage.
    /// Emits X-Routing-Defaults to indicate which defaults were applied: "site,company".
    /// </summary>
    public sealed class SageRoutingHeaderHandler : DelegatingHandler
    {
        private readonly ILogger<SageRoutingHeaderHandler> _log;
        private readonly IHttpContextAccessor _http;
        private readonly IOptions<SageApiSettings> _opt;

        public SageRoutingHeaderHandler(
            ILogger<SageRoutingHeaderHandler> log,
            IHttpContextAccessor http,
            IOptions<SageApiSettings> opt)
        {
            _log = log;
            _http = http;
            _opt = opt;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cfg = _opt.Value;

            // Resolve names once (configurable, with sensible defaults)
            var siteHeaderName = string.IsNullOrWhiteSpace(cfg.SiteHeaderName) ? "X-Site" : cfg.SiteHeaderName!;
            var compHeaderName = string.IsNullOrWhiteSpace(cfg.CompanyHeaderName) ? "X-Company" : cfg.CompanyHeaderName!;

            // 1) Are they already present on this outgoing request?
            bool hasSite = request.Headers.Contains(siteHeaderName);
            bool hasComp = request.Headers.Contains(compHeaderName);

            string? site = null;
            string? comp = null;

            // 2) Ambient worker context (Kafka/Background)
            var ambient = SageCallContext.Current;
            if (!hasSite) site = ambient?.SiteId;
            if (!hasComp) comp = ambient?.CompanyId;

            // 3) Inbound HTTP context (when present)
            var http = _http.HttpContext;
            if (http is not null)
            {
                if (!hasSite && string.IsNullOrWhiteSpace(site))
                    site = TryRead(http, siteHeaderName);
                if (!hasComp && string.IsNullOrWhiteSpace(comp))
                    comp = TryRead(http, compHeaderName);
            }

            // 4) Appsettings defaults
            string applied = "";
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

            // Inject the resolved values (if any)
            TryAdd(request, siteHeaderName, site, ref hasSite);
            TryAdd(request, compHeaderName, comp, ref hasComp);

            if (hasSite && hasComp)
            {
                _log.LogDebug("Routing headers set: {SiteHeader}={Site} {CompHeader}={Company}",
                    siteHeaderName, TryGet(request, siteHeaderName),
                    compHeaderName, TryGet(request, compHeaderName));
            }

            // Sentinel header for observability of defaults
            if (!string.IsNullOrWhiteSpace(applied))
            {
                request.Headers.Remove("X-Routing-Defaults");
                request.Headers.TryAddWithoutValidation("X-Routing-Defaults", applied);
                _log.LogInformation("Routing defaults applied: {Applied} (corr={CorrelationId})",
                    applied, TryGet(request, "X-Correlation-Id"));
            }

            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static string? TryRead(HttpContext ctx, string name) =>
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
