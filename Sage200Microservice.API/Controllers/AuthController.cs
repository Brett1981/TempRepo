using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>Auth utilities for Sage ID.</summary>
    [ApiController]
    [Route("auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly ISageAuthenticationService _auth;

        public AuthController(ISageAuthenticationService auth) => _auth = auth;

        /// <summary>
        /// Redirects the user to Sage ID to grant consent.
        /// </summary>
        [HttpGet("login")]
        public IActionResult Login([FromQuery] string? returnUrl = "/auth/status")
        {
            // generate a simple anti-CSRF state and keep it in a short-lived cookie
            var state = Guid.NewGuid().ToString("N");
            Response.Cookies.Append("oauth_state", state, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10)
            });

            var url = _auth.BuildAuthorizeUrl(state);
            return Redirect(url);
        }

        /// <summary>
        /// OAuth callback target for Sage ID (handles ?code=&state=).
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { message = "Missing ?code." });

            // optional: validate state if we set one
            if (Request.Cookies.TryGetValue("oauth_state", out var expected) && !string.IsNullOrEmpty(expected))
            {
                if (!string.Equals(expected, state, StringComparison.Ordinal))
                    return BadRequest(new { message = "State mismatch." });
                Response.Cookies.Delete("oauth_state");
            }

            var (ok, error) = await _auth.ExchangeCodeForTokensAsync(code, ct);
            if (!ok) return StatusCode(502, new { message = "OAuth code exchange failed.", error });

            // small, human-friendly html page (handy if launched in a popup)
            const string html = @"<!doctype html><meta charset=""utf-8"">
<title>Signed in</title>
<body style=""font:14px/1.4 system-ui,Segoe UI,Arial"">
  <h2>✅ Sage ID connected</h2>
  <p>You can close this tab and return to the app.</p>
</body>";
            return Content(html, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Returns 200 and token info if a token exists; 404 otherwise.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var has = await _auth.HasValidTokenAsync(ct);
            if (!has) return NotFound(new { message = "No OAuth token in store" });

            var info = await _auth.GetTokenInfoAsync(ct);
            return Ok(new
            {
                message = "OAuth token available",
                info?.AccessTokenExpiresUtc,
                info?.HasRefreshToken
            });
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
