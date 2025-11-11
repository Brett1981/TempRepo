using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    // Keep the existing info DTOs in this namespace so the type aliases in the service are correct.
    public sealed class AccessTokenInfo
    {
        public string? Audience { get; init; }
        public string? Issuer { get; init; }
        public string[]? Scopes { get; init; }
        public string? TenantId { get; init; }
        public string? ClientAppId { get; init; }
        public System.DateTimeOffset? ExpiresUtc { get; init; }
        public long? SecondsToExpiry { get; init; }
    }

    public sealed class TokenInfo
    {
        public System.DateTimeOffset AccessTokenExpiresUtc { get; init; }
        public bool HasRefreshToken { get; init; }
    }

    public interface ISageAuthenticationService
    {
        // ------- AuthZ URL builders -------
        // Simple (no PKCE)
        string BuildAuthorizeUrl(string state);

        // PKCE-friendly (backward compatible with controllers that expect it)
        string BuildAuthorizeUrl(string state, string codeChallenge, string codeChallengeMethod = "S256",
                                 IDictionary<string, string>? extraQuery = null);

        // ------- Code exchange -------
        // Simple (no PKCE) — returns (Ok, Error) like your snippet
        Task<(bool Ok, string? Error)> ExchangeCodeForTokensAsync(string code, CancellationToken ct = default);

        // PKCE-friendly (commonly void/throws on failure); use whichever your controllers expect
        Task ExchangeCodeForTokensAsync(string code, string codeVerifier, CancellationToken ct = default);

        // ------- Token access / diagnostics -------
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);
        Task ForceRefreshAsync(CancellationToken ct = default);
        Task<bool> HasValidTokenAsync(CancellationToken ct = default);
        Task<bool> HasRefreshTokenAsync(CancellationToken ct = default);
        Task<TokenInfo?> GetTokenInfoAsync(CancellationToken ct = default);
        Task<AccessTokenInfo?> GetAccessTokenInfoAsync(CancellationToken ct);
        Task<bool> RevokeAccessTokenAsync();
        Task<string> RefreshAccessTokenAsync(CancellationToken ct = default);
    }
}
