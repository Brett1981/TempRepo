using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static System.Net.WebRequestMethods;

namespace Sage200Microservice.Services.Implementations
{
    public class SageApiClient : ISageApiClient
    {
        private readonly ILogger<SageApiClient> _logger;
        private readonly HttpClient _httpClient;
        private readonly ISageAuthenticationService _authService;

        public SageApiClient(
            ILogger<SageApiClient> logger,
            HttpClient httpClient,
            ISageAuthenticationService authService)
        {
            _logger = logger;
            _httpClient = httpClient;
            _authService = authService;
        }

        /// <summary>
        /// Sends an authenticated GET request to the Sage 200 API
        /// </summary>
        /// <typeparam name="T"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint"> The API endpoint (relative to base URL) </param>
        /// <returns> The deserialized response </returns>
        public async Task<T> GetAsync<T>(string endpoint)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var relative = NormalizeRelativePath(endpoint);
                var response = await _httpClient.GetAsync(relative);

                if (!response.IsSuccessStatusCode)
                {
                    // Down-level expected "not found" to Info/Warning to reduce noise
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var level = response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                                    ? LogLevel.Information : LogLevel.Error;
                        _logger.Log(level, "GET {Endpoint} failed {Status}. Not retrying. Body: {Body}",
                            endpoint, (int)response.StatusCode, body);
                    }
                    response.EnsureSuccessStatusCode();
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                {
                    throw new Exception($"Failed to deserialize response from {endpoint}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GET request to {Endpoint}", endpoint);
                throw;
            }
        }

        /// <summary>
        /// Returns raw JSON for /v1/sites so you can confirm Site/Company identifiers to use in headers.
        /// </summary>
        public async Task<string> GetSitesRawAsync(CancellationToken ct = default)
        {
            var token = await _authService.GetAccessTokenAsync(ct);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _httpClient.GetAsync(NormalizeRelativePath("sites"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();
            return body;
        }

        /// <summary>
        /// Sends an authenticated POST request to the Sage 200 API
        /// </summary>
        /// <typeparam name="TRequest"> The type of the request body </typeparam>
        /// <typeparam name="TResponse"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint">    The API endpoint (relative to base URL) </param>
        /// <param name="requestBody"> The request body </param>
        /// <returns> The deserialized response </returns>
        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);
                var response = await _httpClient.PostAsync(relative, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                {
                    throw new Exception($"Failed to deserialize response from {endpoint}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in POST request to {Endpoint}", endpoint);
                throw;
            }
        }

        /// <summary>
        /// Sends an authenticated PUT request to the Sage 200 API
        /// </summary>
        /// <typeparam name="TRequest"> The type of the request body </typeparam>
        /// <typeparam name="TResponse"> The type to deserialize the response to </typeparam>
        /// <param name="endpoint">    The API endpoint (relative to base URL) </param>
        /// <param name="requestBody"> The request body </param>
        /// <returns> The deserialized response </returns>
        public async Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);
                var response = await _httpClient.PutAsync(relative, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                {
                    throw new Exception($"Failed to deserialize response from {endpoint}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PUT request to {Endpoint}", endpoint);
                throw;
            }
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Ensure the request advertises JSON unless caller explicitly set Accept.
            if (!request.Headers.Accept.Any())
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
            }

            // Propagate basic correlation if an Activity is running.
            var activity = Activity.Current;
            if (activity is not null)
            {
                // Do not overwrite if already present.
                if (!request.Headers.Contains("traceparent") && !string.IsNullOrEmpty(activity.Id))
                    request.Headers.TryAddWithoutValidation("traceparent", activity.Id);

                if (!request.Headers.Contains("x-correlation-id"))
                    request.Headers.TryAddWithoutValidation("x-correlation-id", activity.TraceId.ToString());
            }

            // NOTE:
            //  - Caller is responsible for adding X-Site, X-Company, Idempotency-Key and any custom headers.
            //  - Caller should also set Content.Headers.ContentType = application/json for JSON bodies.

            // Send request; don't buffer the entire body (allow streaming for large payloads).
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            // Optional: basic tracing (status + path). Avoid logging bodies here to keep this method generic.
            _logger.LogDebug("Sage request {Method} {Uri} -> {StatusCode}",
                request.Method.Method,
                request.RequestUri,
                (int)response.StatusCode);

            return response; // caller disposes
        }
        /// <summary>
        /// Sends an authenticated DELETE request to the Sage 200 API
        /// </summary>
        /// <param name="endpoint"> The API endpoint (relative to base URL) </param>
        /// <returns> True if the request was successful </returns>
        public async Task<bool> DeleteAsync(string endpoint)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var relative = NormalizeRelativePath(endpoint);
                var response = await _httpClient.DeleteAsync(relative);
                response.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DELETE request to {Endpoint}", endpoint);
                throw;
            }
        }

        public async Task<T> GetAsync<T>(string endpoint, CancellationToken ct)
        {
            const int maxAttempts = 4; // first try + 3 retries
            var rand = new Random();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var token = await _authService.GetAccessTokenAsync(ct);
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                    var relative = NormalizeRelativePath(endpoint);
                    using var resp = await _httpClient.GetAsync(relative, ct);

                    // 401 once? refresh then retry
                    if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 1)
                    {
                        _logger.LogWarning("401 from {Endpoint}; refreshing token and retrying once.", endpoint);
                        await _authService.ForceRefreshAsync(ct);
                        continue;
                    }

                    // Retryable upstream conditions
                    if ((int)resp.StatusCode == 429 ||
                        resp.StatusCode == System.Net.HttpStatusCode.BadGateway ||        // 502
                        resp.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||// 503
                        resp.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)      // 504
                    {
                        var bodyPeek = await resp.Content.ReadAsStringAsync(ct);
                        _logger.LogWarning("Retryable {Status} from {Endpoint} (attempt {Attempt}/{Max}). Body: {Body}",
                            (int)resp.StatusCode, endpoint, attempt, maxAttempts, bodyPeek);

                        if (attempt == maxAttempts)
                            resp.EnsureSuccessStatusCode(); // throw

                        var delay = TimeSpan.FromMilliseconds((int)(Math.Pow(2, attempt - 1) * 500) + rand.Next(0, 250));
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    // Non-success (not retryable)
                    if (!resp.IsSuccessStatusCode)
                    {
                        // Do NOT retry 4xx (except 429 handled above)
                        if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500)
                        {
                            var body = await resp.Content.ReadAsStringAsync(ct);
                            var level = resp.StatusCode == System.Net.HttpStatusCode.NotFound ||
                            resp.StatusCode == System.Net.HttpStatusCode.BadRequest
                            ? LogLevel.Information : LogLevel.Error;
                            _logger.Log(level, "GET {Endpoint} failed {Status}. Not retrying. Body: {Body}",
                            endpoint, (int)resp.StatusCode, body);
                            resp.EnsureSuccessStatusCode(); // throw
                        }

                        resp.EnsureSuccessStatusCode(); // throw for anything else
                    }

                    var json = await resp.Content.ReadAsStringAsync(ct);
                    var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result == null)
                        throw new Exception($"Failed to deserialize response from {endpoint}");

                    return result;
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds((int)(Math.Pow(2, attempt - 1) * 500) + rand.Next(0, 250));
                    _logger.LogWarning("Timeout calling {Endpoint}; backing off {Delay} (attempt {Attempt}/{Max}).",
                        endpoint, delay, attempt, maxAttempts);
                    await Task.Delay(delay, ct);
                }
                catch (HttpRequestException ex) when (
                    attempt < maxAttempts &&
                    // only retry when status is null (socket/DNS/TLS) OR it's 5xx we didn't
                    // explicitly catch
                    (ex.StatusCode is null || (int)ex.StatusCode >= 500)
                )
                {
                    var delay = TimeSpan.FromMilliseconds((int)(Math.Pow(2, attempt - 1) * 500) + rand.Next(0, 250));
                    _logger.LogWarning(ex, "HTTP error calling {Endpoint}; backing off {Delay} (attempt {Attempt}/{Max}).",
                        endpoint, delay, attempt, maxAttempts);
                    await Task.Delay(delay, ct);
                }
            }

            // should never get here
            throw new InvalidOperationException($"Exhausted retries for GET {endpoint}");
        }

        public async Task<(int StatusCode, string Body)> PostForBodyAsync<TRequest>(string relativeUrl, TRequest body, IDictionary<string, string>? headers, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = JsonContent.Create(body)
            };
            if (headers != null)
                foreach (var kv in headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var status = (int)resp.StatusCode;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (status, text);
        }

        /// <summary>
        /// Non-throwing GET helper for callers that want to degrade gracefully on upstream
        /// 5xx/timeouts. Returns (success, result, statusCode) where success=false on upstream flakiness.
        /// </summary>
        public async Task<(bool Success, T? Result, int? StatusCode)> TryGetAsync<T>(string endpoint, CancellationToken ct)
        {
            try
            {
                var res = await GetAsync<T>(endpoint, ct);
                return (true, res, 200);
            }
            catch (TaskCanceledException)
            {
                return (false, default, null);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                return (false, default, ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null);
            }
        }

        private string NormalizeRelativePath(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return string.Empty;

            // If Sage returned an absolute URL in @odata.nextLink, strip down to PathAndQuery
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var abs))
            {
                var rel = abs.PathAndQuery;

                // If the absolute path already includes the base path (/uk/.../accounts/v1/), strip
                // it off
                var basePath = _httpClient.BaseAddress?.AbsolutePath ?? "/";
                if (!basePath.EndsWith("/")) basePath += "/";

                if (rel.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(basePath.Length);

                return rel.TrimStart('/'); // return relative like "customer_views?...“
            }

            // Relative input – just trim any leading slash so we append to /v1/
            return endpoint.TrimStart('/');
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, IDictionary<string, string>? headers, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = JsonContent.Create(body)
            };
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    // Avoid duplicate standard headers; delegate handlers already set auth/site/company.
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct).ConfigureAwait(false))!;
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync(ct);
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);

                using var response = await _httpClient.PostAsync(relative, content, ct);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                    throw new Exception($"Failed to deserialize response from {endpoint}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in POST request to {Endpoint}", endpoint);
                throw;
            }
        }

        public async Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync(ct);
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);

                using var response = await _httpClient.PutAsync(relative, content, ct);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                    throw new Exception($"Failed to deserialize response from {endpoint}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PUT request to {Endpoint}", endpoint);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string endpoint, CancellationToken ct)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync(ct);
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var relative = NormalizeRelativePath(endpoint);
                using var response = await _httpClient.DeleteAsync(relative, ct);
                response.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DELETE request to {Endpoint}", endpoint);
                throw;
            }
        }
    }
}