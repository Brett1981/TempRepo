using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Validators
{
    public sealed class SageAuthDelegatingHandler : DelegatingHandler
    {
        private readonly ISageAuthenticationService _auth;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<SageAuthDelegatingHandler> _log;

        public SageAuthDelegatingHandler(ISageAuthenticationService auth, IHttpContextAccessor http, ILogger<SageAuthDelegatingHandler> log)
        {
            _auth = auth; _http = http; _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Attach current token
            var token = await _auth.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await base.SendAsync(request, ct).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.Unauthorized) return resp;

            // One forced refresh + retry (clone preserves headers)
            var corr = _http.HttpContext?.TraceIdentifier;
            var www = string.Join("; ", resp.Headers.WwwAuthenticate);
            _log.LogWarning("AuthHandler: 401 (first attempt) {Method} {Uri}. WWW-Authenticate: {Auth}. Corr={CorrelationId}",
                request.Method, request.RequestUri, www, corr);

            resp.Dispose();
            await _auth.ForceRefreshAsync(ct).ConfigureAwait(false);

            var refreshed = await _auth.GetAccessTokenAsync(ct).ConfigureAwait(false);
            var retry = request.Clone();
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);

            _log.LogInformation("AuthHandler: Retrying after forced refresh {Method} {Uri}. Corr={CorrelationId}",
                retry.Method, retry.RequestUri, corr);

            return await base.SendAsync(retry, ct).ConfigureAwait(false);
        }
    }

    internal static class HttpRequestMessageCloneExtensions
    {
        public static HttpRequestMessage Clone(this HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri!);
            foreach (var h in request.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (request.Content != null)
            {
                var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var h in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            return clone;
        }
    }
}
