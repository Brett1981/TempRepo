
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Interfaces
{
    public interface ISageApiClient
    {
        /// <summary>
        /// Sends an authenticated GET request to the Sage 200 API
        /// </summary>
        /// <typeparam name="T"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint"> The API endpoint (relative to base URL) </param>
        /// <returns> The deserialized response </returns>
        Task<T> GetAsync<T>(string endpoint);

        Task<T> GetAsync<T>(string endpoint, CancellationToken ct);

        Task<string> GetSitesRawAsync(CancellationToken ct = default);

        /// <summary>
        /// Sends an authenticated POST request to the Sage 200 API
        /// </summary>
        /// <typeparam name="TRequest"> The type of the request body </typeparam>
        /// <typeparam name="TResponse"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint">    The API endpoint (relative to base URL) </param>
        /// <param name="requestBody"> The request body </param>
        /// <returns> The deserialized response </returns>
        Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody);

        /// <summary>
        /// POSTs JSON via the configured pipeline and returns (statusCode, bodyText). Does NOT
        /// throw on non-success, allowing callers to surface upstream diagnostics.
        /// </summary>
        Task<(int StatusCode, string Body)> PostForBodyAsync<TRequest>(string relativeUrl, TRequest body, IDictionary<string, string>? headers, CancellationToken ct);

        /// <summary>
        /// POSTs a JSON body to the given relative Sage URL using the configured pipeline (Bearer,
        /// X-Site, X-Company, retry/jitter, 401 refresh, ProblemDetails mapping), allowing
        /// per-request headers (e.g., Idempotency-Key).
        /// </summary>
        Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, IDictionary<string, string>? headers, CancellationToken ct);

        /// <summary>
        /// Sends an authenticated PUT request to the Sage 200 API
        /// </summary>
        /// <typeparam name="TRequest"> The type of the request body </typeparam>
        /// <typeparam name="TResponse"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint">    The API endpoint (relative to base URL) </param>
        /// <param name="requestBody"> The request body </param>
        /// <returns> The deserialized response </returns>
        Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody);

        /// <summary>
        /// Sends an authenticated DELETE request to the Sage 200 API
        /// </summary>
        /// <param name="endpoint"> The API endpoint (relative to base URL) </param>
        /// <returns> True if the request was successful </returns>
        Task<bool> DeleteAsync(string endpoint);

        Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct);

        Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct);

        Task<bool> DeleteAsync(string endpoint, CancellationToken ct);
        /// <summary>
        /// Sends an HTTP request to the Sage 200 Accounts API and returns the raw response.
        /// </summary>
        /// <param name="request">The HTTP request to send. Must not be reused after sending.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>The HTTP response message from Sage 200.</returns>
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

        private void LogHeaderHintIfMissing(HttpRequestMessage req, ILogger logger, SageApiSettings cfg)
        {
            bool missingSite = !req.Headers.Contains(cfg.SiteHeaderName);
            bool missingCompany = !req.Headers.Contains(cfg.CompanyHeaderName);
            bool missingApiKey = !req.Headers.Contains(cfg.ApiKeyHeaderName);

            if (missingSite || missingCompany || missingApiKey)
            {
                logger.LogDebug("ISageApiClient sending request with headers present? Site:{Site} Company:{Company} ApiKey:{ApiKey}",
                    !missingSite, !missingCompany, !missingApiKey);
            }
        }
    }
}