using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Proactively maintains OAuth tokens in the background so API calls never need to
    /// trigger a manual re-login. Uses only the methods that exist in your current
    /// ISageAuthenticationService: GetTokenInfoAsync, HasValidTokenAsync, GetAccessTokenAsync, ForceRefreshAsync.
    /// </summary>
    public sealed class TokenMaintenanceService : BackgroundService
    {
        private readonly ILogger<TokenMaintenanceService> _logger;
        private readonly IOptions<TokenMaintenanceOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Random _rng = new();

        public TokenMaintenanceService(
            ILogger<TokenMaintenanceService> logger,
            IOptions<TokenMaintenanceOptions> options,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _options = options;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var opt = _options.Value;

            if (!opt.Enabled)
            {
                _logger.LogInformation("Token maintenance disabled by configuration.");
                return;
            }

            _logger.LogInformation(
                "Token maintenance starting: checkPeriod={Check}s, proactiveRefresh={Refresh}s, startupDelay={Delay}s, jitter={Jitter}s",
                opt.CheckPeriodSeconds, opt.ProactiveRefreshSeconds, opt.StartupDelaySeconds, opt.JitterSeconds);

            if (opt.StartupDelaySeconds > 0)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(opt.StartupDelaySeconds), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }

            // Initial pass: ensure a valid token is available ASAP.
            await RunOnceAsync(stoppingToken);

            // Periodic loop
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = NextDelay(TimeSpan.FromSeconds(opt.CheckPeriodSeconds), opt.JitterSeconds);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }

                await RunOnceAsync(stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<ISageAuthenticationService>();

            try
            {
                // Ask for current snapshot (HasRefreshToken + AccessTokenExpiresUtc)
                var info = await auth.GetTokenInfoAsync(ct);

                // If there's no refresh token at all, we can't automatically recover.
                if (!info.HasRefreshToken)
                {
                    // Still try to ensure a valid access token once (may be cached/valid already)
                    var hasValid = await auth.HasValidTokenAsync(ct);
                    if (!hasValid)
                    {
                        _logger.LogCritical(
                            "OAuth refresh token is missing. Automatic refresh is impossible. " +
                            "An interactive login is required to re-establish a refresh token.");
                    }
                    else
                    {
                        _logger.LogDebug("Access token currently valid but no refresh token is stored.");
                    }

                    return; // nothing more we can do without a refresh token
                }

                // If we have a refresh token:
                // 1) If access token valid and not near expiry, do nothing
                // 2) If near expiry, proactively refresh
                // 3) If invalid, force a refresh now

                var isValid = await auth.HasValidTokenAsync(ct);
                var proactiveWindow = TimeSpan.FromSeconds(_options.Value.ProactiveRefreshSeconds);

                if (isValid)
                {
                    if (info.AccessTokenExpiresUtc != default)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var timeLeft = info.AccessTokenExpiresUtc - now;

                        if (timeLeft <= TimeSpan.Zero)
                        {
                            _logger.LogWarning("Access token already expired; forcing refresh.");
                            await ForceRefreshSafeAsync(auth, ct);
                        }
                        else if (timeLeft <= proactiveWindow)
                        {
                            _logger.LogInformation(
                                "Access token expires in {Seconds:F0}s (<= {Proactive}s). Proactively refreshing.",
                                timeLeft.TotalSeconds, proactiveWindow.TotalSeconds);
                            await ForceRefreshSafeAsync(auth, ct);
                        }
                        else
                        {
                            _logger.LogDebug("Access token healthy. Time remaining: {Seconds:F0}s.", timeLeft.TotalSeconds);
                        }
                    }
                    else
                    {
                        // No expiry info returned — make sure we can still obtain a token (no-op if valid)
                        _logger.LogDebug("No expiry info available. Touching token to ensure validity.");
                        await auth.GetAccessTokenAsync(ct);
                    }
                }
                else
                {
                    _logger.LogWarning("Access token invalid. Attempting refresh now.");
                    await ForceRefreshSafeAsync(auth, ct);
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                // Never crash the background worker; just log and try again next tick.
                _logger.LogError(ex, "Unexpected error during token maintenance run.");
            }
        }

        private async Task ForceRefreshSafeAsync(ISageAuthenticationService auth, CancellationToken ct)
        {
            try
            {
                // Force a refresh (no return value in your implementation)
                await auth.ForceRefreshAsync(ct);

                // Touch the token to ensure we have a usable access token now
                await auth.GetAccessTokenAsync(ct);

                // Snapshot for logging (uses your existing GetTokenInfoAsync + AccessTokenExpiresUtc)
                var snap = await auth.GetTokenInfoAsync(ct);

                // Also verify validity to be explicit in logs
                var valid = await auth.HasValidTokenAsync(ct);
                if (valid)
                {
                    _logger.LogInformation(
                        "Access token refreshed successfully. New expiry UTC: {Expiry:o}",
                        snap.AccessTokenExpiresUtc);
                }
                else
                {
                    _logger.LogWarning(
                        "ForceRefreshAsync completed but token still invalid. Expiry (if known): {Expiry:o}",
                        snap.AccessTokenExpiresUtc);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Access token refresh failed.");
            }
        }


        private TimeSpan NextDelay(TimeSpan basePeriod, int jitterSeconds)
        {
            if (jitterSeconds <= 0) return basePeriod;
            var j = _rng.Next(-jitterSeconds, jitterSeconds + 1);
            var ms = Math.Max(0, (int)basePeriod.TotalMilliseconds + (j * 1000));
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
