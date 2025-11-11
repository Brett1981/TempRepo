using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Auth;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("auth")]
    [Produces("application/json")]
    public sealed class AuthLoginController : ControllerBase
    {
        private readonly ISageAuthenticationService _auth;
        private readonly IOAuthStateStore _stateStore;
        private readonly SageApiSettings _sage;

        public AuthLoginController(
            ISageAuthenticationService auth,
            IOAuthStateStore stateStore,
            IOptions<SageApiSettings> sageOptions)
        {
            _auth = auth;
            _stateStore = stateStore;
            _sage = sageOptions.Value;
        }

        /// <summary>Starts the interactive OAuth flow. Redirects to Sage with a one-time state.</summary>
        [HttpGet("login")]
        public async Task<IActionResult> Login(CancellationToken ct)
        {
            // Create a one-time state with 5-minute TTL
            var state = await _stateStore.CreateAsync(TimeSpan.FromMinutes(5), ct);
            var authUrl = _auth.BuildAuthorizeUrl(state); // your updated service builds the URL
            return Redirect(authUrl);
        }

        /// <summary>OAuth redirect URI configured at Sage. Validates 'state' then exchanges 'code'.</summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return BadRequest(new { title = "Missing code/state", status = 400 });

            // Validate & consume state (prevents replay)
            var ok = await _stateStore.TryConsumeAsync(state, ct);
            if (!ok)
                return BadRequest(new { title = "OAuth state mismatch", status = 400 });

            // Exchange code for tokens (your service persists refresh token)
            var (exchanged, error) = await _auth.ExchangeCodeForTokensAsync(code, ct);
            if (!exchanged)
                return StatusCode((int)HttpStatusCode.BadGateway, new { title = "Token exchange failed", status = 502, detail = error });

            // Redirect to a friendly page (or return JSON)
            // Return JSON by default so Postman etc. are happy
            return Ok(new { message = "Login complete", success = true });
        }
    }
}
