// =========================================================================================================
// ISageApiClient extension
// - Many parts of the codebase use _sage.PostForBodyAsync(...).
// =========================================================================================================

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sage200Microservice.Services.Interfaces
{
    public static class SageApiClientGetExtensions
    {
        /// <summary>
        /// GET helper that returns (statusCode, bodyText) given a relative path (with query) and headers.
        /// </summary>
        public static async Task<(int StatusCode, string BodyText)> GetForBodyAsync(
            this ISageApiClient client,
            string relativePathWithQuery,
            IDictionary<string, string> headers,
            CancellationToken ct)
        {
            // Reuse the client's internal HttpClient
            using var req = new HttpRequestMessage(HttpMethod.Get, relativePathWithQuery);
            foreach (var h in headers)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);

            var resp = await client.SendAsync(req, ct);
            var txt = await resp.Content.ReadAsStringAsync(ct);
            return ((int)resp.StatusCode, txt);
        }
    }
}
