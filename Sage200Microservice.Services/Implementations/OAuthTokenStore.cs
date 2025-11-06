// Services/Implementations/OAuthTokenStore.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>Durable store for OAuth refresh token (encrypted at rest).</summary>
    public sealed class OAuthTokenStore : IOAuthTokenStore
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDataProtector _protector;
        private readonly ILogger<OAuthTokenStore> _logger;

        // If you’ll support multiple audiences, inject these via options instead of consts.
        private const string ProviderName = "Sage";
        private const string AudienceName = "s200ukipd/sage200";

        public OAuthTokenStore(
                IServiceScopeFactory scopeFactory,
                IDataProtectionProvider dp,
                ILogger<OAuthTokenStore> logger) // Inject logger
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _protector = dp.CreateProtector("Sage200Microservice.OAuth.RefreshToken");
        }

        public async Task<bool> HasRefreshTokenAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var row = await db.OAuthTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Provider == ProviderName && x.Audience == AudienceName, ct);

            // Use the correct method name in the log message
            _logger.LogDebug("HasRefreshTokenAsync: Found token record in DB. ProtectedRefreshToken IsNullOrEmpty: {IsNullOrEmpty}",
                string.IsNullOrEmpty(row?.ProtectedRefreshToken));

            if (row?.ProtectedRefreshToken is null)
            {
                return false; // No protected token found
            }

            try
            {
                // Try to unprotect, but we only care *if* it succeeds, not the value itself
                _ = _protector.Unprotect(row.ProtectedRefreshToken);
                _logger.LogDebug("HasRefreshTokenAsync: Successfully unprotected token check. Token exists and is valid.");
                return true; // Unprotection succeeded, therefore a valid token exists
            }
            catch (Exception ex) // Catch specific DataProtection exceptions if needed
            {
                // Use the correct method name in the log message
                _logger.LogError(ex, "HasRefreshTokenAsync: Failed to unprotect refresh token during check. DataProtection keys might be invalid or missing.");
                return false; // Unprotection failed, treat as if no valid token exists
            }
        }

        public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var row = await db.OAuthTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Provider == ProviderName && x.Audience == AudienceName, ct);

            if (row?.ProtectedRefreshToken is null) return null;

            try { return _protector.Unprotect(row.ProtectedRefreshToken); }
            catch { return null; }
        }

        public async Task SaveAsync(
            string refreshToken,
            DateTimeOffset? accessTokenExpiresUtc = null,
            string? scopeName = null,
            CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var protectedToken = _protector.Protect(refreshToken);

            var row = await db.OAuthTokens
                .SingleOrDefaultAsync(x => x.Provider == ProviderName && x.Audience == AudienceName, ct);

            if (row is null)
            {
                row = new OAuthToken
                {
                    // Id is identity – do NOT set
                    Provider = ProviderName,
                    Audience = AudienceName,
                    ProtectedRefreshToken = protectedToken,
                    AccessTokenExpiresUtc = accessTokenExpiresUtc,
                    Scope = scopeName,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                await db.OAuthTokens.AddAsync(row, ct);
            }
            else
            {
                row.ProtectedRefreshToken = protectedToken;
                row.AccessTokenExpiresUtc = accessTokenExpiresUtc;
                row.Scope = scopeName;
                row.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task ClearAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var row = await db.OAuthTokens
                .SingleOrDefaultAsync(x => x.Provider == ProviderName && x.Audience == AudienceName, ct);

            if (row is not null)
            {
                db.OAuthTokens.Remove(row);
                await db.SaveChangesAsync(ct);
            }
        }

        public async Task<(bool HasToken, DateTimeOffset? AccessTokenExpiresUtc)> GetInfoAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var row = await db.OAuthTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Provider == ProviderName && x.Audience == AudienceName, ct);

            var has = false;
            if (row?.ProtectedRefreshToken is not null)
            {
                try { _ = _protector.Unprotect(row.ProtectedRefreshToken); has = true; }
                catch { has = false; }
            }

            return (has, row?.AccessTokenExpiresUtc);
        }
    }
}
