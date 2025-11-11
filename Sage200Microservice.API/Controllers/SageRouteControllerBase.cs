using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services;           // your ISageApiClient
using Sage200Microservice.Services.Http;      // for header names, if centralised
using Sage200Microservice.Services.Interfaces;
using System.Text.Json;

namespace Sage200Microservice.API.Controllers.Infrastructure
{
    public abstract class SageRouteControllerBase : ControllerBase
    {
        private readonly ISageApiClient _sage;
        private readonly ILogger _log;

        protected SageRouteControllerBase(ISageApiClient sage, ILogger log)
        {
            _sage = sage;
            _log = log;
        }

        /// <summary>
        /// Ensures X-Site & X-Company are present. If missing, fetches /sites and picks a site+company,
        /// then stamps them into HttpContext.Items so SageRoutingHeaderHandler can inject them.
        /// </summary>
        protected async Task EnsureRoutingAsync(CancellationToken ct)
        {
            // Already on the inbound request? (Swagger can pass these in)
            var haveSite = Request.Headers.TryGetValue("X-Site", out var siteH) && !string.IsNullOrWhiteSpace(siteH);
            var haveComp = Request.Headers.TryGetValue("X-Company", out var compH) && !string.IsNullOrWhiteSpace(compH);

            if (haveSite && haveComp)
            {
                HttpContext.Items["X-Site"] = siteH.ToString();
                HttpContext.Items["X-Company"] = compH.ToString();
                return;
            }

            // Not present → discover from /sites (uses your working flow)
            var sitesJson = await _sage.GetAsync<string>("/sites", ct);
            using var doc = JsonDocument.Parse(sitesJson);

            // your existing parsing code …
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("No sites returned for this token.");

            var siteId = first.TryGetProperty("site_id", out var sProp) ? sProp.GetString() : null;
            var companyId = first.TryGetProperty("company_id", out var cProp) ? cProp.GetRawText() : null;

            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(companyId))
                throw new InvalidOperationException("Could not resolve site/company from /sites.");

            HttpContext.Items["X-Site"] = siteId!;
            HttpContext.Items["X-Company"] = companyId!;

            _log.LogInformation("Resolved routing: X-Site={Site} X-Company={Company}", siteId, companyId);
        }
    }
}
