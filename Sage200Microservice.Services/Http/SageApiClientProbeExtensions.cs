using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.Services.Http
{
    public static class SageApiClientProbeExtensions
    {
        /// <summary>
        /// Raw GET that returns (status, body, headers) as strings for diagnostics.
        /// Relies on the typed HttpClient behind ISageApiClient (auth/logging headers already applied).
        /// </summary>
        public static async Task<(int StatusCode, string Body, IDictionary<string, string[]> Headers)>
            RawGetAsync(this ISageApiClient client, string relativePath, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, relativePath);
            var resp = await client.SendAsync(req, ct).ConfigureAwait(false); // this uses your existing pipeline

            var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in resp.Headers)
                headers[h.Key] = h.Value.ToArray();
            if (resp.Content != null)
            {
                foreach (var h in resp.Content.Headers)
                    headers[h.Key] = h.Value.ToArray();
            }

            return ((int)resp.StatusCode, body, headers);
        }
    }
}
