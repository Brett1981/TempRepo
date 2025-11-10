using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// Final step in the pipeline: stamps Authorization and ensures X-Site/X-Company/X-Api-Key are present.
    /// Works for both HTTP-initiated and background/Kafka calls.
    /// </summary>
    public sealed class SageAuthDelegatingHandler : DelegatingHandler
    {
        private readonly ISageAuthenticationService _auth;
        private readonly IHttpContextAccessor _http;
        private readonly IHostEnvironment _env;
        private readonly SageApiSettings _cfg;
        private readonly ILogger<SageAuthDelegatingHandler> _log;

        public SageAuthDelegatingHandler(
            ISageAuthenticationService auth,
            IHttpContextAccessor http,
            IOptions<SageApiSettings> cfg,
            IHostEnvironment env,
            ILogger<SageAuthDelegatingHandler> log)
        {
            _auth = auth;
            _http = http;
            _env = env;
            _cfg = cfg.Value;
            _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Attach Bearer token
            var token = await _auth.GetAccessTokenAsync(ct);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Ensure routing & caller headers
            EnsureRoutingHeaders(request);
            EnsureApiKeyHeader(request);

            var corr = _http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("D");

            var response = await base.SendAsync(request, ct);

            if ((int)response.StatusCode == 401)
            {
                var www = response.Headers.WwwAuthenticate.ToString();
                _log.LogWarning("AuthHandler: 401 (first attempt) {Method} {Uri}. WWW-Authenticate: {AuthHeader}. CorrelationId: {CorrelationId}. Forcing refresh.",
                    request.Method, request.RequestUri, www, corr);

                await _auth.ForceRefreshAsync(ct);

                // retry once with a fresh token
                var retry = request.Clone(); // extension method you already have; if not, add one as before
                var newToken = await _auth.GetAccessTokenAsync(ct);
                retry.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newToken);
                EnsureRoutingHeaders(retry);
                EnsureApiKeyHeader(retry);

                _log.LogInformation("AuthHandler: Retrying after forced refresh {Method} {Uri}. CorrelationId: {CorrelationId}",
                    retry.Method, retry.RequestUri, corr);

                response.Dispose();
                return await base.SendAsync(retry, ct);
            }

            return response;
        }

        private void EnsureRoutingHeaders(HttpRequestMessage request)
        {
            // X-Site
            if (!request.Headers.Contains(_cfg.SiteHeaderName) && !string.IsNullOrWhiteSpace(_cfg.SiteId))
                request.Headers.TryAddWithoutValidation(_cfg.SiteHeaderName, _cfg.SiteId);

            // X-Company
            if (!request.Headers.Contains(_cfg.CompanyHeaderName) && !string.IsNullOrWhiteSpace(_cfg.CompanyId))
                request.Headers.TryAddWithoutValidation(_cfg.CompanyHeaderName, _cfg.CompanyId);
        }

        private void EnsureApiKeyHeader(HttpRequestMessage request)
        {
            // If we don't want to forward our internal api key to Sage, remove it and return.
            if (!_cfg.ForwardApiKeyToSage)
            {
                if (request.Headers.Contains(_cfg.ApiKeyHeaderName))
                    request.Headers.Remove(_cfg.ApiKeyHeaderName);
                return;
            }

            // Otherwise (opt-in), inject only in Development if missing and allowed.
            if (request.Headers.Contains(_cfg.ApiKeyHeaderName))
                return;

            if (string.Equals(_env.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                && _cfg.AllowDevelopmentFallbackApiKey
                && !string.IsNullOrWhiteSpace(_cfg.DevelopmentDefaultApiKey))
            {
                request.Headers.TryAddWithoutValidation(_cfg.ApiKeyHeaderName, _cfg.DevelopmentDefaultApiKey);
            }
        }
    }

    internal static class HttpRequestMessageCloneExtensions
    {
        /// <summary>
        /// Clones the HttpRequestMessage for safe one-shot retry while keeping headers and content stream.
        /// </summary>
        public static HttpRequestMessage Clone(this HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri!);

            // headers
            foreach (var h in request.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

            // content headers + body (buffered once)
            if (request.Content != null)
            {
                var stream = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(stream);
                foreach (var h in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            return clone;
        }
    }
}
