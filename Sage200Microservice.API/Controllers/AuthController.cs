using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Authentication utilities for the Sage 200 integration (status + revoke).
    /// </summary>
    [ApiController]
    [Route("auth")]
    [Produces("application/json")]
    public sealed class AuthController : ControllerBase
    {
        private readonly ISageAuthenticationService _auth;
        private readonly SageApiSettings _sageApi;

        /// <summary>
        /// Creates the controller with access to the authentication service and API settings (for host context).
        /// </summary>
        public AuthController(
            ISageAuthenticationService auth,
            IOptions<SageApiSettings> sageApiOptions)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _sageApi = (sageApiOptions ?? throw new ArgumentNullException(nameof(sageApiOptions))).Value;
        }

        /// <summary>
        /// Returns 200 and enriched token info if a token exists; 404 otherwise.
        /// Adds decoded, non-sensitive JWT diagnostics (aud/iss/scopes/expiry).
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var has = await _auth.HasValidTokenAsync(ct);
            if (!has)
                return NotFound(new { message = "No OAuth token in store" });

            var basic = await _auth.GetTokenInfoAsync(ct);             // expiry + hasRefresh flag
            var decoded = await _auth.GetAccessTokenInfoAsync(ct);     // audience/scopes/issuer/etc.

            var response = new
            {
                message = "OAuth token available",
                AccessTokenExpiresUtc = basic?.AccessTokenExpiresUtc ?? decoded?.ExpiresUtc,
                HasRefreshToken = basic?.HasRefreshToken ?? false,
                Token = new
                {
                    Audience = decoded?.Audience,
                    Scopes = decoded?.Scopes,
                    Issuer = decoded?.Issuer,
                    TenantId = decoded?.TenantId,
                    ClientAppId = decoded?.ClientAppId,
                    ExpiresUtc = decoded?.ExpiresUtc,
                    SecondsToExpiry = decoded?.SecondsToExpiry,
                    BaseUrl = _sageApi?.BaseUrl,
                    BaseUrlHost = TryParseHost(_sageApi?.BaseUrl)
                }
            };

            return Ok(response);

            static string? TryParseHost(string? url)
            {
                if (string.IsNullOrWhiteSpace(url)) return null;
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
            }
        }

        /// <summary>
        /// Revokes the access token (best effort) and clears local cache.
        /// </summary>
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke(CancellationToken _)
        {
            var ok = await _auth.RevokeAccessTokenAsync();
            return Ok(new { success = ok });
        }
    }
}
