// --------------------------------------------------------------------------------------
// Sage OAuth service with durable refresh-token storage.
// - Persists refresh token via IOAuthTokenStore (DB-backed, encrypted at rest).
// - Caches access token in memory and refreshes when near expiry.
// --------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Implementations
{
    public sealed class SageAuthenticationService : ISageAuthenticationService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ILogger<SageAuthenticationService> _logger;
        private readonly HttpClient _http;
        private readonly SageApiSettings _settings;
        private readonly IOAuthTokenStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);

        // In-memory access token cache
        private string? _accessToken;
        private DateTimeOffset _accessExpiresUtc;

        public SageAuthenticationService(
            ILogger<SageAuthenticationService> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<SageApiSettings> options,
            IOAuthTokenStore store)
        {
            _logger = logger;
            _http = httpClientFactory.CreateClient("SageAuth"); // Assumes named client "SageAuth" is registered
            _settings = options.Value;
            _store = store;

            _logger.LogInformation(
                "OAuth cfg => RedirectUri={Redirect} | AuthZ={AuthZ} | Token={Token} | Scopes={Scopes} | Audience={Audience}",
                _settings.RedirectUri, _settings.AuthorizationEndpoint, _settings.TokenEndpoint,
                _settings.Scopes, _settings.Audience);
        }

        // ------------------------ Public API ------------------------

        public string BuildAuthorizeUrl(string state)
        {
            if (string.IsNullOrWhiteSpace(_settings.AuthorizationEndpoint) ||
                string.IsNullOrWhiteSpace(_settings.ClientId) ||
                string.IsNullOrWhiteSpace(_settings.RedirectUri) ||
                string.IsNullOrWhiteSpace(_settings.Audience))
            {
                throw new InvalidOperationException(
                    "SageApi settings are missing (AuthorizationEndpoint/ClientId/RedirectUri/Audience). " +
                    "Check appsettings:SageApi and binding.");
            }

            var enc = UrlEncoder.Default;

            // Ensure scopes are encoded properly
            var scopes = string.IsNullOrWhiteSpace(_settings.Scopes) ? "openid offline_access" : _settings.Scopes;

            return $"{_settings.AuthorizationEndpoint}" +
                   $"?audience={enc.Encode(_settings.Audience)}" +
                   $"&client_id={enc.Encode(_settings.ClientId)}" +
                   $"&response_type=code" +
                   $"&redirect_uri={enc.Encode(_settings.RedirectUri)}" +
                   $"&scope={enc.Encode(scopes)}" + // Use encoded scopes variable
                   $"&state={enc.Encode(state)}";
        }

        // Back-compat alias - delegates to the primary method
        public Task<(bool Ok, string? Error)> ExchangeAuthorizationCodeAsync(string code, CancellationToken ct = default)
            => ExchangeCodeForTokensAsync(code, ct);

        /// <summary>
        /// Exchanges ?code= for tokens; persists the refresh token; caches access token in memory.
        /// </summary>
        public async Task<(bool Ok, string? Error)> ExchangeCodeForTokensAsync(string code, CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("ExchangeCodeForTokensAsync: Attempting to exchange auth code."); // Log Start
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = _settings.ClientId,
                    // DO NOT LOG ClientSecret
                    ["client_secret"] = _settings.ClientSecret,
                    ["redirect_uri"] = _settings.RedirectUri,
                    ["code"] = code
                };
                if (!string.IsNullOrWhiteSpace(_settings.Audience))
                    form["audience"] = _settings.Audience;

                var token = await PostFormAsync<TokenResponse>(_settings.TokenEndpoint, form, ct);

                // Update in-memory access token
                _accessToken = token.AccessToken;
                _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
                LogTokenPayloadForDiagnostics(_accessToken); // Log payload after successful acquisition

                // Persist (encrypted) refresh token
                if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    await _store.SaveAsync(token.RefreshToken!,
                                           _accessExpiresUtc,
                                           token.Scope,
                                           ct);
                }

                _logger.LogInformation("ExchangeCodeForTokensAsync: Code exchange successful. New access token expires at {ExpiryUtc}. Refresh token saved.",
                    _accessExpiresUtc); // Log Success
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExchangeCodeForTokensAsync: Code exchange failed."); // Log Failure
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Returns a valid access token; refreshes using the stored refresh token if needed.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        {
            // Log Cache Check
            _logger.LogDebug("GetAccessTokenAsync: Checking token cache. Current expiry (UTC): {ExpiryUtc}", _accessExpiresUtc);

            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2)) // Use 2 min buffer
            {
                // Log Cache Hit
                _logger.LogInformation("GetAccessTokenAsync: Using cached access token valid until {ExpiryUtc}", _accessExpiresUtc);
                return _accessToken!;
            }

            // Log Cache Miss/Near Expiry
            _logger.LogInformation("GetAccessTokenAsync: Cached token missing or near expiry ({ExpiryUtc}). Attempting refresh.",
                 _accessExpiresUtc);
            return await RefreshAccessTokenAsync(ct);
        }

        public async Task ForceRefreshAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("ForceRefreshAsync called. Clearing in-memory access token cache.");
            _accessToken = null;
            _accessExpiresUtc = DateTimeOffset.MinValue;
            // GetAccessTokenAsync will now trigger a refresh
            _ = await GetAccessTokenAsync(ct);
        }

        public async Task<bool> HasValidTokenAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
                return true;

            return await _store.HasRefreshTokenAsync(ct); // Relies on store's check (includes decryption attempt)
        }

        public async Task<TokenInfo?> GetTokenInfoAsync(CancellationToken ct = default)
        {
            var hasAccess = !string.IsNullOrWhiteSpace(_accessToken);
            var accessExp = hasAccess ? _accessExpiresUtc : (DateTimeOffset?)null;

            var info = await _store.GetInfoAsync(ct);

            return new TokenInfo
            {
                // Provide consistent expiry, preferring in-memory if available and valid
                AccessTokenExpiresUtc = (accessExp.HasValue && accessExp > DateTimeOffset.UtcNow)
                                         ? accessExp.Value
                                         : (info.AccessTokenExpiresUtc ?? DateTimeOffset.MinValue),
                HasRefreshToken = info.HasToken
            };
        }

        /// <summary>
        /// Uses the persisted refresh token to obtain a new access token and saves it (handles rotation).
        /// </summary>
        public async Task<string> RefreshAccessTokenAsync(CancellationToken ct = default)
        {
            // Log Refresh Start
            _logger.LogInformation("RefreshAccessTokenAsync: Attempting token refresh.");

            var refresh = await _store.GetRefreshTokenAsync(ct);
            // Log Refresh Token Status (NOT the token itself)
            _logger.LogDebug("RefreshAccessTokenAsync: Refresh token retrieved from store. IsNullOrWhiteSpace: {IsNullOrWhiteSpace}", string.IsNullOrWhiteSpace(refresh));

            if (string.IsNullOrWhiteSpace(refresh))
            {
                _logger.LogError("RefreshAccessTokenAsync: No refresh token available in store. Cannot refresh.");
                throw new InvalidOperationException("No refresh token available. Please /auth/login.");
            }

            await _gate.WaitAsync(ct); // Semaphore gate starts here
            try
            {
                // Double-check cache inside lock
                if (!string.IsNullOrWhiteSpace(_accessToken) &&
                    _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
                {
                    _logger.LogInformation("RefreshAccessTokenAsync: Another thread refreshed token while waiting for semaphore. Using newly cached token.");
                    return _accessToken!;
                }

                // Log Token Endpoint Call
                _logger.LogInformation("RefreshAccessTokenAsync: Calling token endpoint: {TokenEndpoint}", _settings.TokenEndpoint);

                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    // DO NOT LOG refresh_token
                    ["refresh_token"] = refresh!,
                    ["client_id"] = _settings.ClientId,
                    // DO NOT LOG client_secret
                    ["client_secret"] = _settings.ClientSecret
                };
                if (!string.IsNullOrWhiteSpace(_settings.Audience))
                    form["audience"] = _settings.Audience;

                var token = await PostFormAsync<TokenResponse>(_settings.TokenEndpoint, form, ct);

                _accessToken = token.AccessToken;
                _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
                LogTokenPayloadForDiagnostics(_accessToken); // Log decoded payload

                // Handle potential refresh token rotation
                var toPersist = string.IsNullOrWhiteSpace(token.RefreshToken) ? refresh! : token.RefreshToken!;
                await _store.SaveAsync(toPersist, _accessExpiresUtc, token.Scope, ct);

                // Log Refresh Success
                _logger.LogInformation("RefreshAccessTokenAsync: Token refresh successful. New access token expires at {ExpiryUtc}. Refresh token {(WasRotated)} rotated.",
                     _accessExpiresUtc, string.IsNullOrWhiteSpace(token.RefreshToken) ? "was NOT" : "WAS");
                return _accessToken!;
            }
            catch (HttpRequestException ex) // Catch specific exception from PostFormAsync
            {
                // PostFormAsync already logged details, just log the context here
                _logger.LogError(ex, "RefreshAccessTokenAsync: Token refresh POST failed."); // Log Refresh Failure
                // Clear potentially stale cached token on failure
                _accessToken = null;
                _accessExpiresUtc = DateTimeOffset.MinValue;
                throw; // Rethrow to signal failure
            }
            finally
            {
                _gate.Release(); // Semaphore gate ends here
            }
        }

        /// <summary>
        /// Best-effort server revocation (if configured) + local clear.
        /// </summary>
        public async Task<bool> RevokeAccessTokenAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.RevocationEndpoint) &&
                    !string.IsNullOrWhiteSpace(_accessToken))
                {
                    _logger.LogInformation("RevokeAccessTokenAsync: Attempting server-side revocation at {RevocationEndpoint}.", _settings.RevocationEndpoint);
                    var form = new Dictionary<string, string>
                    {
                        // DO NOT LOG token value
                        ["token"] = _accessToken!,
                        ["client_id"] = _settings.ClientId,
                        // DO NOT LOG client secret
                        ["client_secret"] = _settings.ClientSecret
                    };

                    _ = await PostFormAsync<JsonElement>(_settings.RevocationEndpoint!, form, CancellationToken.None);
                    _logger.LogDebug("RevokeAccessTokenAsync: Server-side revocation POST completed (best effort).");
                }

                await _store.ClearAsync();
                _accessToken = null;
                _accessExpiresUtc = DateTimeOffset.MinValue;

                _logger.LogInformation("Tokens cleared{Revoked}.",
                    string.IsNullOrWhiteSpace(_settings.RevocationEndpoint) ? " (server revoke skipped)" : " and server revoked (best effort)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token revocation failed (continuing).");
                // Attempt local clear even if server fails
                try { await _store.ClearAsync(); } catch { /* ignore */ }
                _accessToken = null;
                _accessExpiresUtc = DateTimeOffset.MinValue;
                return false;
            }
        }

        // ------------------------ Internals ------------------------

        private async Task<T> PostFormAsync<T>(string url, IDictionary<string, string> form, CancellationToken ct)
        {
            // Use Basic Auth for token endpoint
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var content = new FormUrlEncodedContent(form);
            using var resp = await _http.PostAsync(url, content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            // _logger is guaranteed non-null
            if (!resp.IsSuccessStatusCode)
            {
                // Log truncated body on failure
                _logger.LogError("POST {Url} failed: {Status} {Reason} {BodyPreview}", url, (int)resp.StatusCode, resp.ReasonPhrase, Truncate(body, 1024));
                throw new HttpRequestException($"POST {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(body, JsonOpts);
                if (result is null)
                {
                    _logger.LogError("Could not deserialize token response from {Url}. Body: {BodyPreview}", url, Truncate(body, 1024));
                    throw new InvalidOperationException($"Could not deserialize token response from {url}.");
                }
                return result;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON Deserialization failed for token response from {Url}. Body: {BodyPreview}", url, Truncate(body, 1024));
                throw new InvalidOperationException($"Could not deserialize token response from {url}.", jsonEx);
            }
        }

        // Added Helper - Log decoded token payload (audience, scope, expiry)
        private void LogTokenPayloadForDiagnostics(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return;
            try
            {
                var parts = accessToken.Split('.');
                if (parts.Length < 2) return;

                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var payloadDoc = JsonDocument.Parse(payloadJson);

                string aud = payloadDoc.RootElement.TryGetProperty("aud", out var audEl) ? audEl.ToString() : "N/A";
                string scope = payloadDoc.RootElement.TryGetProperty("scope", out var scopeEl) ? scopeEl.ToString() : "N/A";
                long exp = payloadDoc.RootElement.TryGetProperty("exp", out var expEl) && expEl.ValueKind == JsonValueKind.Number ? expEl.GetInt64() : 0;
                DateTimeOffset expiryTime = DateTimeOffset.FromUnixTimeSeconds(exp);

                _logger.LogInformation("RefreshAccessTokenAsync: Decoded token payload - Audience: {Audience}, Scope: {Scope}, Expiry: {Expiry}", aud, scope, expiryTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RefreshAccessTokenAsync: Failed to decode token payload for diagnostics.");
            }
        }

        // Added Helper - Base64Url decoding
        private static byte[] Base64UrlDecode(string input)
        {
            string output = input.Replace('-', '+').Replace('_', '/');
            switch (output.Length % 4)
            {
                case 0: break;
                case 2: output += "=="; break;
                case 3: output += "="; break;
                default: throw new FormatException("Illegal base64url string!");
            }
            return Convert.FromBase64String(output);
        }

        // Added Helper - Simple string truncation
        private static string Truncate(string? value, int maxLength) =>
            value?.Length > maxLength ? value.Substring(0, maxLength) + "..." : (value ?? "");

        // ------------------------ DTOs ------------------------

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }
        }
    }

    // Public model used by controllers (unchanged)
    public sealed class TokenInfo
    {
        public DateTimeOffset AccessTokenExpiresUtc { get; init; }
        public bool HasRefreshToken { get; init; }
    }
}