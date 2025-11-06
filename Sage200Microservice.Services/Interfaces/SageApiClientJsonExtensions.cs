using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Convenience extension methods that provide a JSON-centric naming for the existing
    /// <see cref="ISageApiClient"/> operations. These simply delegate to the interface's
    /// core methods and do not change behavior.
    /// </summary>
    public static class SageApiClientJsonExtensions
    {
        /// <summary>
        /// Sends an authenticated POST request with a JSON body and deserializes the JSON response.
        /// Delegates to <see cref="ISageApiClient.PostAsync{TRequest, TResponse}(string, TRequest, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="TRequest">Type of the request body being serialized to JSON.</typeparam>
        /// <typeparam name="TResponse">Type to deserialize the JSON response to.</typeparam>
        /// <param name="client">The <see cref="ISageApiClient"/> instance.</param>
        /// <param name="endpoint">Relative Sage API endpoint (e.g., "sop_orders_status").</param>
        /// <param name="requestBody">Request payload object to serialize as JSON.</param>
        /// <param name="ct">Cancellation token.</param>
        public static Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            this ISageApiClient client,
            string endpoint,
            TRequest requestBody,
            CancellationToken ct = default)
            => client.PostAsync<TRequest, TResponse>(endpoint, requestBody, ct);

        /// <summary>
        /// Sends an authenticated PUT request with a JSON body and deserializes the JSON response.
        /// Delegates to <see cref="ISageApiClient.PutAsync{TRequest, TResponse}(string, TRequest, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="TRequest">Type of the request body being serialized to JSON.</typeparam>
        /// <typeparam name="TResponse">Type to deserialize the JSON response to.</typeparam>
        /// <param name="client">The <see cref="ISageApiClient"/> instance.</param>
        /// <param name="endpoint">Relative Sage API endpoint.</param>
        /// <param name="requestBody">Request payload object to serialize as JSON.</param>
        /// <param name="ct">Cancellation token.</param>
        public static Task<TResponse> PutJsonAsync<TRequest, TResponse>(
            this ISageApiClient client,
            string endpoint,
            TRequest requestBody,
            CancellationToken ct = default)
            => client.PutAsync<TRequest, TResponse>(endpoint, requestBody, ct);

        /// <summary>
        /// Sends an authenticated GET request and deserializes the JSON response.
        /// Delegates to <see cref="ISageApiClient.GetAsync{T}(string, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response to.</typeparam>
        /// <param name="client">The <see cref="ISageApiClient"/> instance.</param>
        /// <param name="endpoint">Relative Sage API endpoint.</param>
        /// <param name="ct">Cancellation token.</param>
        public static Task<T> GetJsonAsync<T>(
            this ISageApiClient client,
            string endpoint,
            CancellationToken ct = default)
            => client.GetAsync<T>(endpoint, ct);
    }
}
