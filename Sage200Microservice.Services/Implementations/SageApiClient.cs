using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.Services.Implementations
{
    public sealed class SageApiClient : ISageApiClient
    {
        private readonly ILogger<SageApiClient> _logger;
        private readonly HttpClient _httpClient;

        public SageApiClient(
            ILogger<SageApiClient> logger,
            HttpClient httpClient,
            ISageAuthenticationService _ /* not used now: handlers manage auth */)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>Sends a GET and deserializes JSON to T (throws on non-success).</summary>
        public async Task<T> GetAsync<T>(string endpoint)
        {
            try
            {
                var relative = NormalizeRelativePath(endpoint);
                using var response = await _httpClient.GetAsync(relative);

                if (!response.IsSuccessStatusCode)
                {
                    // Down-level expected "not found" to Info/Warning to reduce noise
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var level = response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                                        ? LogLevel.Information
                                        : LogLevel.Error;
                        _logger.Log(level, "GET {Endpoint} failed {Status}. Not retrying. Body: {Body}",
                            endpoint, (int)response.StatusCode, body);
                    }
                    response.EnsureSuccessStatusCode();
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result == null)
                    throw new Exception($"Failed to deserialize response from {endpoint}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GET request to {Endpoint}", endpoint);
                throw;
            }
        }

        /// <summary>Raw JSON for /v1/sites to confirm Site/Company identifiers.</summary>
        public async Task<string> GetSitesRawAsync(CancellationToken ct = default)
        {
            using var resp = await _httpClient.GetAsync(NormalizeRelativePath("sites"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();
            return body;
        }

        /// <summary>Sends POST with JSON body and deserializes JSON to TResponse.</summary>
        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody)
        {
            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);
                using var response = await _httpClient.PostAsync(relative, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

        /// <summary>Sends PUT with JSON body and deserializes JSON to TResponse.</summary>
        public async Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody)
        {
            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var relative = NormalizeRelativePath(endpoint);
                using var response = await _httpClient.PutAsync(relative, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

        /// <summary>Low-level send that preserves streaming and adds minimal tracing.</summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!request.Headers.Accept.Any())
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

            var activity = Activity.Current;
            if (activity is not null)
            {
                if (!request.Headers.Contains("traceparent") && !string.IsNullOrEmpty(activity.Id))
                    request.Headers.TryAddWithoutValidation("traceparent", activity.Id);

                if (!request.Headers.Contains("x-correlation-id"))
                    request.Headers.TryAddWithoutValidation("x-correlation-id", activity.TraceId.ToString());
            }

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Sage request {Method} {Uri} -> {StatusCode}",
                request.Method.Method,
                request.RequestUri,
                (int)response.StatusCode);

            return response; // caller disposes
        }

        /// <summary>DELETE with throw-on-non-success.</summary>
        public async Task<bool> DeleteAsync(string endpoint)
        {
            try
            {
                var relative = NormalizeRelativePath(endpoint);
                using var response = await _httpClient.DeleteAsync(relative);
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DELETE request to {Endpoint}", endpoint);
                throw;
            }
        }

        // --- Overloads with CancellationToken + retry policy (kept) ---

        public async Task<T> GetAsync<T>(string endpoint, CancellationToken ct)
        {
            const int maxAttempts = 4; // first try + 3 retries
            var rand = new Random();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var relative = NormalizeRelativePath(endpoint);
                    using var resp = await _httpClient.GetAsync(relative, ct);

                    if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 1)
                    {
                        var www = string.Join("; ", resp.Headers.WwwAuthenticate);
                        var peek = await resp.Content.ReadAsStringAsync(ct);
                        _logger.LogWarning("401 from {Endpoint}. WWW-Authenticate: {Auth}. Body: {Body}",
                            endpoint, www, peek.Length > 300 ? peek[..300] + "…" : peek);
                        resp.EnsureSuccessStatusCode();
                    }

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

                    if (!resp.IsSuccessStatusCode)
                    {
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
                    var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                    (ex.StatusCode is null || (int)ex.StatusCode >= 500))
                {
                    var delay = TimeSpan.FromMilliseconds((int)(Math.Pow(2, attempt - 1) * 500) + rand.Next(0, 250));
                    _logger.LogWarning(ex, "HTTP error calling {Endpoint}; backing off {Delay} (attempt {Attempt}/{Max}).",
                        endpoint, delay, attempt, maxAttempts);
                    await Task.Delay(delay, ct);
                }
            }

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

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, IDictionary<string, string>? headers, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = JsonContent.Create(body)
            };
            if (headers != null)
                foreach (var kv in headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct).ConfigureAwait(false))!;
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var relative = NormalizeRelativePath(endpoint);

            using var response = await _httpClient.PostAsync(relative, content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
                throw new Exception($"Failed to deserialize response from {endpoint}");
            return result;
        }

        public async Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest requestBody, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var relative = NormalizeRelativePath(endpoint);

            using var response = await _httpClient.PutAsync(relative, content, ct);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
                throw new Exception($"Failed to deserialize response from {endpoint}");
            return result;
        }

        public async Task<bool> DeleteAsync(string endpoint, CancellationToken ct)
        {
            var relative = NormalizeRelativePath(endpoint);
            using var response = await _httpClient.DeleteAsync(relative, ct);
            response.EnsureSuccessStatusCode();
            return true;
        }

        private string NormalizeRelativePath(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return string.Empty;

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var abs))
            {
                var rel = abs.PathAndQuery;
                var basePath = _httpClient.BaseAddress?.AbsolutePath ?? "/";
                if (!basePath.EndsWith("/")) basePath += "/";
                if (rel.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(basePath.Length);
                return rel.TrimStart('/');
            }

            return endpoint.TrimStart('/');
        }


    }
}
