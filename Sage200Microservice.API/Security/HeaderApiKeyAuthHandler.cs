using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Models; // SageApiSettings

namespace Sage200Microservice.API.Security
{
    public sealed class HeaderApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Scheme = "HeaderApiKey";
        private readonly IOptions<SageApiSettings> _sage;

        public HeaderApiKeyAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IOptions<SageApiSettings> sage)
            : base(options, logger, encoder)
        {
            _sage = sage;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Allow anonymous when header is missing; endpoints with [Authorize] will still challenge
            if (!Request.Headers.TryGetValue("X-Api-Key", out var key) || string.IsNullOrWhiteSpace(key))
                return Task.FromResult(AuthenticateResult.NoResult());

            // Identify the caller as "apikey"
            var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, "apikey")
                };

            // Optional: promote to Admin if configured
            var adminKeys = _sage.Value.AdminApiKeys ?? Array.Empty<string>();
            if (adminKeys.Contains(key.ToString(), StringComparer.Ordinal))
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, Scheme); // Scheme = your handler's scheme name
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
