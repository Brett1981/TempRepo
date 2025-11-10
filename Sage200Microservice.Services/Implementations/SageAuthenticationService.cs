// --------------------------------------------------------------------------------------
// Sage OAuth service with durable refresh-token storage.
// - Persists refresh token via IOAuthTokenStore (DB-backed, encrypted at rest).
// - Caches access token in memory and refreshes when near expiry.
// --------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Auth;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
// Ensure our method signatures use the EXACT same types as the interface:
using AccessTokenInfo = Sage200Microservice.Services.Interfaces.AccessTokenInfo;
using TokenInfo = Sage200Microservice.Services.Interfaces.TokenInfo;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Concrete OAuth helper used by the outbound Sage pipeline and controller diagnostics.
    /// Matches ISageAuthenticationService signatures used across the solution.
    /// </summary>
    public sealed class SageAuthenticationService : ISageAuthenticationService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ILogger<SageAuthenticationService> _logger;
        private readonly HttpClient _http; // named client "SageAuth"
        private readonly SageApiSettings _settings;
        private readonly IOAuthTokenStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private string? _accessToken;
        private DateTimeOffset _accessExpiresUtc;

        public SageAuthenticationService(
            ILogger<SageAuthenticationService> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<SageApiSettings> options,
            IOAuthTokenStore store)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _http = (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient("SageAuth");
            _settings = (options ?? throw new ArgumentNullException(nameof(options))).Value
                        ?? throw new ArgumentNullException(nameof(options));

            // Light normalization (no external extension method needed)
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl) && !_settings.BaseUrl.EndsWith("/"))
                _settings.BaseUrl += "/";
            if (string.IsNullOrWhiteSpace(_settings.Scopes))
                _settings.Scopes = "openid offline_access";

            _store = store ?? throw new ArgumentNullException(nameof(store));

            _logger.LogInformation(
                "OAuth cfg => RedirectUri={Redirect} | AuthZ={AuthZ} | Token={Token} | Scopes={Scopes} | Audience={Audience}",
                _settings.RedirectUri, _settings.AuthorizationEndpoint, _settings.TokenEndpoint,
                _settings.Scopes, _settings.Audience);
        }

        public string BuildAuthorizeUrl(string state)
        {
            var enc = UrlEncoder.Default;

            var scopes = string.IsNullOrWhiteSpace(_settings.Scopes)
                ? "openid offline_access"
                : _settings.Scopes;

            return $"{_settings.AuthorizationEndpoint}" +
                   $"?audience={enc.Encode(_settings.Audience ?? string.Empty)}" +
                   $"&client_id={enc.Encode(_settings.ClientId ?? string.Empty)}" +
                   $"&response_type=code" +
                   $"&redirect_uri={enc.Encode(_settings.RedirectUri ?? string.Empty)}" +
                   $"&scope={enc.Encode(scopes)}" +
                   $"&state={enc.Encode(state ?? string.Empty)}";
        }

        public Task<(bool Ok, string? Error)> ExchangeCodeForTokensAsync(string code, CancellationToken ct = default)
            => ExchangeAuthorizationCodeAsync(code, ct);

        private async Task<(bool Ok, string? Error)> ExchangeAuthorizationCodeAsync(string code, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("ExchangeCodeForTokensAsync: Attempting to exchange auth code.");
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = _settings.ClientId ?? string.Empty,
                    ["client_secret"] = _settings.ClientSecret ?? string.Empty,
                    ["redirect_uri"] = _settings.RedirectUri ?? string.Empty,
                    ["code"] = code ?? string.Empty
                };
                if (!string.IsNullOrWhiteSpace(_settings.Audience))
                    form["audience"] = _settings.Audience!;

                var token = await PostFormAsync<TokenResponse>(_settings.TokenEndpoint!, form, ct).ConfigureAwait(false);

                _accessToken = token.AccessToken;
                _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
                LogTokenPayloadForDiagnostics(_accessToken);

                if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                    await _store.SaveAsync(token.RefreshToken!, _accessExpiresUtc, token.Scope, ct).ConfigureAwait(false);

                _logger.LogInformation("ExchangeCodeForTokensAsync: Code exchange successful. New access token expires at {ExpiryUtc}.", _accessExpiresUtc);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExchangeCodeForTokensAsync: Code exchange failed.");
                return (false, ex.Message);
            }
        }

        public async Task<bool> HasRefreshTokenAsync(CancellationToken ct = default)
        {
            try { return await _store.HasRefreshTokenAsync(ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HasRefreshTokenAsync: failed to query token store.");
                return false;
            }
        }

        public async Task<AccessTokenInfo?> GetAccessTokenInfoAsync(CancellationToken ct)
        {
            var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
            return TokenIntrospection.TryDecode(token);
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("GetAccessTokenAsync: Cache expiry (UTC): {ExpiryUtc}", _accessExpiresUtc);

            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                _logger.LogInformation("GetAccessTokenAsync: Using cached access token valid until {ExpiryUtc}", _accessExpiresUtc);
                return _accessToken!;
            }

            _logger.LogInformation("GetAccessTokenAsync: Cached token missing or near expiry ({ExpiryUtc}). Attempting refresh.", _accessExpiresUtc);
            return await RefreshAccessTokenAsync(ct).ConfigureAwait(false);
        }

        public async Task ForceRefreshAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("ForceRefreshAsync: Clearing in-memory access token cache.");
            _accessToken = null;
            _accessExpiresUtc = DateTimeOffset.MinValue;
            _ = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        }

        public async Task<bool> HasValidTokenAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
                return true;

            return await _store.HasRefreshTokenAsync(ct).ConfigureAwait(false);
        }

        public async Task<TokenInfo?> GetTokenInfoAsync(CancellationToken ct = default)
        {
            var hasAccess = !string.IsNullOrWhiteSpace(_accessToken);
            var accessExp = hasAccess ? _accessExpiresUtc : (DateTimeOffset?)null;

            var persisted = await _store.GetInfoAsync(ct).ConfigureAwait(false);

            return new TokenInfo
            {
                AccessTokenExpiresUtc =
                    (accessExp.HasValue && accessExp > DateTimeOffset.UtcNow)
                        ? accessExp.Value
                        : (persisted.AccessTokenExpiresUtc ?? DateTimeOffset.MinValue),
                HasRefreshToken = persisted.HasToken
            };
        }

        public async Task<string> RefreshAccessTokenAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("RefreshAccessTokenAsync: Attempting token refresh.");

            var refresh = await _store.GetRefreshTokenAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("RefreshAccessTokenAsync: Refresh token present? {Present}", !string.IsNullOrWhiteSpace(refresh));
            if (string.IsNullOrWhiteSpace(refresh))
                throw new InvalidOperationException("No refresh token available. Please /auth/login.");

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrWhiteSpace(_accessToken) &&
                    _accessExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
                {
                    _logger.LogInformation("RefreshAccessTokenAsync: Token already refreshed by another thread.");
                    return _accessToken!;
                }

                _logger.LogInformation("RefreshAccessTokenAsync: Calling token endpoint: {TokenEndpoint}", _settings.TokenEndpoint);

                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh!,
                    ["client_id"] = _settings.ClientId ?? string.Empty,
                    ["client_secret"] = _settings.ClientSecret ?? string.Empty
                };
                if (!string.IsNullOrWhiteSpace(_settings.Audience))
                    form["audience"] = _settings.Audience!;

                var token = await PostFormAsync<TokenResponse>(_settings.TokenEndpoint!, form, ct).ConfigureAwait(false);

                _accessToken = token.AccessToken;
                _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
                LogTokenPayloadForDiagnostics(_accessToken);

                var toPersist = string.IsNullOrWhiteSpace(token.RefreshToken) ? refresh! : token.RefreshToken!;
                await _store.SaveAsync(toPersist, _accessExpiresUtc, token.Scope, ct).ConfigureAwait(false);

                _logger.LogInformation("RefreshAccessTokenAsync: Success. Access token expires {ExpiryUtc}.", _accessExpiresUtc);
                return _accessToken!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> RevokeAccessTokenAsync()
        {
            try
            {
                // No RevocationEndpoint in settings — do a local clear only.
                await _store.ClearAsync().ConfigureAwait(false);
                _accessToken = null;
                _accessExpiresUtc = DateTimeOffset.MinValue;
                _logger.LogInformation("RevokeAccessTokenAsync: tokens cleared (local).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RevokeAccessTokenAsync: local clear failed.");
                _accessToken = null;
                _accessExpiresUtc = DateTimeOffset.MinValue;
                return false;
            }
        }

        private async Task<T> PostFormAsync<T>(string url, IDictionary<string, string> form, CancellationToken ct)
        {
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var content = new FormUrlEncodedContent(form);
            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var preview = body.Length > 1024 ? body[..1024] + "..." : body;
                _logger.LogError("POST {Url} failed: {Status} {Reason} {BodyPreview}", url, (int)resp.StatusCode, resp.ReasonPhrase, preview);
                throw new HttpRequestException($"POST {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);
            }

            return JsonSerializer.Deserialize<T>(body, JsonOpts)
                   ?? throw new InvalidOperationException($"Could not deserialize token response from {url}.");
        }

        private void LogTokenPayloadForDiagnostics(string? accessToken)
        {
            var info = TokenIntrospection.TryDecode(accessToken);
            if (info is null) return;

            _logger.LogInformation("AuthToken: host={Host} aud={Aud} iss={Iss} tid={Tid} scp={Scp} exp={ExpUtc:o}",
                "api.columbus.sage.com",
                info.Audience ?? "(null)",
                info.Issuer ?? "(null)",
                info.TenantId ?? "(null)",
                info.Scopes is null ? "(null)" : string.Join(' ', info.Scopes),
                info.ExpiresUtc ?? DateTimeOffset.MinValue);
        }

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
            [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
            [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
            [JsonPropertyName("scope")] public string? Scope { get; set; }
        }
    }
}
