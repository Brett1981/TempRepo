using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Contract for acquiring, refreshing and revoking OAuth tokens for Sage 200.
    /// Must match the concrete methods already implemented by SageAuthenticationService.
    /// </summary>
    public interface ISageAuthenticationService
    {
        /// <summary>
        /// Builds a user-interactive authorization URL for the configured Sage IdP.
        /// </summary>
        /// <param name="state">Opaque state value for CSRF protection.</param>
        string BuildAuthorizeUrl(string state);

        /// <summary>
        /// Returns a decoded, non-sensitive view of the current access token.
        /// Returns null when no token present.
        /// </summary>
        Task<AccessTokenInfo?> GetAccessTokenInfoAsync(CancellationToken ct);

        /// <summary>
        /// Gets a valid access token string, refreshing as required.
        /// Throws if token cannot be acquired.
        /// </summary>
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);

        /// <summary>
        /// Forces the next call to mint a fresh access token (clears in-memory cache).
        /// </summary>
        Task ForceRefreshAsync(CancellationToken ct = default);

        /// <summary>
        /// True when a valid (unexpired) access token exists in memory/store.
        /// </summary>
        Task<bool> HasValidTokenAsync(CancellationToken ct = default);

        /// <summary>
        /// Lightweight info (expiry/refresh presence) without exposing the token.
        /// </summary>
        Task<TokenInfo?> GetTokenInfoAsync(CancellationToken ct = default);

        /// <summary>
        /// Refreshes the access token immediately, returning the new token string.
        /// </summary>
        Task<string> RefreshAccessTokenAsync(CancellationToken ct = default);

        /// <summary>
        /// Attempts to revoke the current access token (best effort) and clears local cache.
        /// </summary>
        Task<bool> RevokeAccessTokenAsync();
    }

    /// <summary>
    /// Non-sensitive, decoded view of the current access token for diagnostics.
    /// (Kept in the interface file so both API and Services can reference it without circular deps.)
    /// </summary>
    public sealed class AccessTokenInfo
    {
        /// <summary>Primary audience/resource for which the token was minted (e.g., "s200ukipd/sage200").</summary>
        public string? Audience { get; init; }

        /// <summary>Token issuer (e.g., "https://id.sage.com/").</summary>
        public string? Issuer { get; init; }

        /// <summary>Tenant Id if present in claims, otherwise null.</summary>
        public string? TenantId { get; init; }

        /// <summary>Client app id used for minting.</summary>
        public string? ClientAppId { get; init; }

        /// <summary>Scopes contained in the token (non-sensitive).</summary>
        public string[]? Scopes { get; init; }

        /// <summary>UTC expiry of the access token.</summary>
        public DateTimeOffset? ExpiresUtc { get; init; }

        /// <summary>Seconds remaining to expiry at the time of decoding (diagnostic only).</summary>
        public double? SecondsToExpiry { get; init; }
    }

    /// <summary>
    /// Minimal status information about the token cached in store.
    /// </summary>
    public sealed class TokenInfo
    {
        /// <summary>UTC expiry of the current access token if known.</summary>
        public DateTimeOffset AccessTokenExpiresUtc { get; init; }

        /// <summary>True if a refresh token exists with the store.</summary>
        public bool HasRefreshToken { get; init; }
    }
}
