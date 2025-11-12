using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// Adds Authorization bearer, X-Site, X-Company and correlation headers.
    /// Retries once on 401 after a forced token refresh.
    /// Also captures WWW-Authenticate and short upstream previews for diagnostics.
    /// </summary>
    public sealed class SageAuthDelegatingHandler : DelegatingHandler
    {
        private readonly ILogger<SageAuthDelegatingHandler> _logger;
        private readonly ISageAuthenticationService _auth;
        private readonly SageApiSettings _cfg;

        public const string CorrelationHeader = "X-Correlation-Id";

        public SageAuthDelegatingHandler(
            ILogger<SageAuthDelegatingHandler> logger,
            ISageAuthenticationService auth,
            IOptions<SageApiSettings> cfg)
        {
            _logger = logger;
            _auth = auth;
            _cfg = cfg.Value ?? new SageApiSettings();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var correlationId = GetCorrelationId(request); // Helper to get correlation ID
            _logger.LogInformation("AuthHandler: Preparing request for {Method} {Uri}. CorrelationId: {CorrelationId}",
                request.Method, request.RequestUri, correlationId);

            await StampAsync(request, ct);

            _logger.LogInformation("AuthHandler: Stamped request with token retrieved via GetAccessTokenAsync. CorrelationId: {CorrelationId}", correlationId);

            var resp = await base.SendAsync(request, ct);

            // 401 once? Force refresh and retry exactly once.
            if ((int)resp.StatusCode == 401)
            {
                var wwwAuth = resp.Headers.WwwAuthenticate?.ToString() ?? "";
                _logger.LogWarning("AuthHandler: Received 401 (first attempt) for {Method} {Uri}. WWW-Authenticate: {AuthHeader}. CorrelationId: {CorrelationId}. Triggering token refresh.",
                    request.Method, request.RequestUri, Truncate(wwwAuth, 1024), correlationId);
                resp.Dispose();

                await _auth.ForceRefreshAsync(ct);

                using var retry = await CloneAsync(request, ct);
                var retryCorrelationId = GetCorrelationId(retry); // Re-get in case clone changes it (unlikely)
                await StampAsync(retry, ct);

                _logger.LogInformation("AuthHandler: Retrying request after forced refresh for {Method} {Uri}. CorrelationId: {CorrelationId}",
                    retry.Method, retry.RequestUri, retryCorrelationId);

                resp = await base.SendAsync(retry, ct);

                if ((int)resp.StatusCode == 401)
                {
                    var retryWwwAuth = resp.Headers.WwwAuthenticate?.ToString() ?? "";
                    _logger.LogError("AuthHandler: Received 401 (on retry) for {Method} {Uri}. WWW-Authenticate: {AuthHeader}. CorrelationId: {CorrelationId}. Giving up.",
                        retry.Method, retry.RequestUri, Truncate(retryWwwAuth, 1024), retryCorrelationId);
                }
            }

            return resp;
        }

        private async Task StampAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Always prefer JSON
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Correlation
            if (!request.Headers.Contains(CorrelationHeader))
            {
                request.Headers.Add(CorrelationHeader, Guid.NewGuid().ToString("N"));
            }

            // Site/Company (names come from settings)
            AddHeaderIfNotEmpty(request, _cfg.SiteHeaderName, _cfg.SiteId);
            AddHeaderIfNotEmpty(request, _cfg.CompanyHeaderName, _cfg.CompanyId);
        }

        private static void AddHeaderIfNotEmpty(HttpRequestMessage req, string? name, string? value)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) return;

            if (req.Headers.Contains(name))
                req.Headers.Remove(name);

            req.Headers.Add(name, value);
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            foreach (var h in request.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

            if (request.Content is not null)
            {
                var ms = new MemoryStream();
                await request.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;
                clone.Content = new StreamContent(ms);

                foreach (var h in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            return clone;
        }

        // Helper to safely get Correlation ID
        private static string GetCorrelationId(HttpRequestMessage request)
        {
            if (request.Headers.TryGetValues(CorrelationHeader, out var values))
            {
                return values.FirstOrDefault() ?? "N/A";
            }
            return "N/A";
        }

        // Simple string truncation helper
        private static string Truncate(string? value, int maxLength) =>
value?.Length > maxLength ? value.Substring(0, maxLength) + "..." : (value ?? "");
    }
}
