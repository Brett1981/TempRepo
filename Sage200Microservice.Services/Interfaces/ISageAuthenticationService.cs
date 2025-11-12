using Sage200Microservice.Services.Implementations;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Contract for acquiring, refreshing and revoking OAuth tokens for Sage 200.
    /// </summary>
    public interface ISageAuthenticationService
    {
        /// <summary>Builds the interactive authorization URL.</summary>
        string BuildAuthorizeUrl(string state);

        /// <summary>Exchanges the authorization <c>code</c> for tokens and persists them.</summary>
        Task<(bool Ok, string? Error)> ExchangeCodeForTokensAsync(string code, CancellationToken ct = default);

        /// <summary>Gets a valid access token, refreshing if necessary.</summary>
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);

        /// <summary>Forces a refresh of the access token.</summary>
        Task ForceRefreshAsync(CancellationToken ct = default);

        /// <summary>Returns true when a usable token exists (access or refresh).</summary>
        Task<bool> HasValidTokenAsync(CancellationToken ct = default);

        /// <summary>Returns simple diagnostics about the cached token (or null).</summary>
        Task<TokenInfo?> GetTokenInfoAsync(CancellationToken ct = default);

        /// <summary>Refreshes the token using the refresh token.</summary>
        Task<string> RefreshAccessTokenAsync(CancellationToken ct = default);

        /// <summary>Revokes the access token remotely and clears the local cache.</summary>
        Task<bool> RevokeAccessTokenAsync();
    }



}