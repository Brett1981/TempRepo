using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Authentication utilities for the Sage 200 integration (login, callback, status, revoke).
    /// </summary>
    [ApiController]
    [Route("auth")]
    [Produces("application/json")]
    public sealed class AuthController : ControllerBase
    {
        private const string PkceCookieName = "s200_pkce_v";
        private const string StateCookieName = "s200_oauth_state";
        private readonly ISageAuthenticationService _auth;
        private readonly SageApiSettings _sageApi;
        private readonly ILogger<AuthController> _log;

        public AuthController(
            ISageAuthenticationService auth,
            IOptions<SageApiSettings> sageApiOptions,
            ILogger<AuthController> log)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _sageApi = (sageApiOptions ?? throw new ArgumentNullException(nameof(sageApiOptions))).Value;
            _log = log;
        }



        // ----------- STATUS (unchanged, kept) -----------

        /// <summary>
        /// Returns 200 and enriched token info if a token exists; 404 otherwise.
        /// Adds decoded, non-sensitive JWT diagnostics (aud/iss/scopes/expiry).
        /// </summary>
        [HttpGet("status")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var has = await _auth.HasValidTokenAsync(ct);
            if (!has)
                return NotFound(new { message = "No OAuth token in store" });

            var basic = await _auth.GetTokenInfoAsync(ct);         // expiry + hasRefresh flag
            var decoded = await _auth.GetAccessTokenInfoAsync(ct); // audience/scopes/issuer/etc.

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
        }

        // ----------- REVOKE (unchanged) -----------

        /// <summary>Revokes the access token (best effort) and clears local cache.</summary>
        [HttpPost("revoke")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<IActionResult> Revoke(CancellationToken ct)
        {
            var ok = await _auth.RevokeAccessTokenAsync();
            return Ok(new { success = ok });
        }

        // ----------- helpers -----------

        private static byte[] CreateRandomBytes(int len)
        {
            var b = new byte[len];
            RandomNumberGenerator.Fill(b);
            return b;
        }
        private static byte[] Sha256(string input) => SHA256.HashData(Encoding.UTF8.GetBytes(input));
        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool TimeSafeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string? TryParseHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
        }
    }
}
