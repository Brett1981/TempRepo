using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Propagates inbound X-Correlation-Id (or generates one) to outbound requests.
    /// </summary>
    public sealed class CorrelationIdHandler : DelegatingHandler
    {
        private const string HeaderName = "X-Correlation-Id";
        private readonly IHttpContextAccessor _http;

        public CorrelationIdHandler(IHttpContextAccessor http) => _http = http;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fromInbound = _http.HttpContext?.Request?.Headers[HeaderName].ToString();
            var corr = string.IsNullOrWhiteSpace(fromInbound) ? System.Diagnostics.Activity.Current?.Id ?? System.Guid.NewGuid().ToString() : fromInbound!;
            if (!request.Headers.Contains(HeaderName))
                request.Headers.Add(HeaderName, corr);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
