
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Middleware
{
    public sealed class ApiKeyAuthenticationHandler
        : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IApiKeyRepository _repo;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _config;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IApiKeyRepository repo,
            IHostEnvironment env,
            IConfiguration config)
            : base(options, logger, encoder, clock)
        {
            _repo = repo;
            _env = env;
            _config = config;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // 1) Get key from header
            var key = Request.Headers[ApiKeyAuthenticationDefaults.HeaderName].ToString();

            // 2) Dev auto-key
            if (string.IsNullOrWhiteSpace(key) && _env.IsDevelopment())
            {
                key = _config["SageApi:DevelopmentDefaultApiKey"] ?? "lJ9CvaBZyV3dWYYPeUKpqlvFV2AWOvpm7Daaat9nxYU";
                // Optionally inject so downstream code that reads the header still sees it
                Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = key;
            }

            if (string.IsNullOrWhiteSpace(key))
                return AuthenticateResult.NoResult(); // will trigger 401 by the [Authorize] pipeline

            // 3) Validate against repository
            var ct = Context.RequestAborted;
            var entity = await _repo.GetByKeyAsync(key, ct) ?? await _repo.GetByPreviousKeyAsync(key, ct);
            var valid = await _repo.IsValidKeyAsync(key, ct);
            if (entity is null || !valid)
                return AuthenticateResult.Fail("Invalid API key.");

            await _repo.UpdateLastUsedAsync(key, ct);

            // 4) Claims principal (include appId so controllers don’t need to re-query)
            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, entity.Id.ToString()),
            new Claim("appId", entity.Id.ToString()),
            new Claim("clientName", entity.ClientName ?? string.Empty),
            // Satisfy policy:
            new Claim("role", "ApiUser"),                 // simple role-based
            new Claim("scope", "sage200microservice.api") // or scope-based
        };
            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);
            return AuthenticateResult.Success(ticket);
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers["WWW-Authenticate"] = ApiKeyAuthenticationDefaults.Scheme;
            return Task.CompletedTask;
        }
    }
}
