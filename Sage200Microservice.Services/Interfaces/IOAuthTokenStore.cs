// Services/Interfaces/IOAuthTokenStore.cs
using Sage200Microservice.Services.Implementations;
using Sage200Microservice.Services.Models; // only if you keep TokenInfo here; otherwise remove
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Durable storage for the Sage OAuth refresh token (and a little metadata).
    /// Keep the refresh token encrypted at rest.
    /// </summary>
    public interface IOAuthTokenStore
    {
        /// <summary>Returns true if a refresh token exists.</summary>
        Task<bool> HasRefreshTokenAsync(CancellationToken ct = default);

        /// <summary>Gets the raw refresh token (decrypted) or null.</summary>
        Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);

        /// <summary>Persists/updates the refresh token and optional access expiry/scope.</summary>
        Task SaveAsync(string refreshToken,
                       DateTimeOffset? accessTokenExpiresUtc = null,
                       string? scope = null,
                       CancellationToken ct = default);

        /// <summary>Clears any persisted token.</summary>
        Task ClearAsync(CancellationToken ct = default);

        /// <summary>Optionally: read back lightweight info for diagnostics.</summary>
        Task<(bool HasToken, DateTimeOffset? AccessTokenExpiresUtc)> GetInfoAsync(CancellationToken ct = default);

    }
}
