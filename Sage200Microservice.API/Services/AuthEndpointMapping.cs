using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Services
{
    public static class AuthEndpointMapping
    {

        private static bool _mapped; // guard against accidental double mapping from multiple invocations

        /// <summary>
        /// Maps /auth/login and /auth/callback as minimal endpoints.
        /// Excluded from Swagger to avoid any potential conflicts.
        /// </summary>
        public static void MapSageAuth(this WebApplication app)
        {
            if (_mapped) return; // guard against accidental double-mapping
            _mapped = true;

            var auth = app.MapGroup("/auth").WithTags("Auth (Internal)");

            // GET /auth/login -> redirect to Sage ID
            auth.MapGet("/login", (ISageAuthenticationService svc) =>
                Results.Redirect(svc.BuildAuthorizeUrl(Guid.NewGuid().ToString("N"))))
                .WithName("Auth_Login_Minimal")
                .ExcludeFromDescription(); // hides from Swagger only

            // GET /auth/callback -> receive ?code= and exchange for tokens
            auth.MapGet("/callback", async (HttpContext http, ISageAuthenticationService svc, CancellationToken ct) =>
            {
                var code = http.Request.Query["code"].ToString();
                var error = http.Request.Query["error"].ToString();
                var desc = http.Request.Query["error_description"].ToString();

                if (!string.IsNullOrWhiteSpace(error))
                    return Results.Problem($"Sage error: {error} ({desc})");

                if (string.IsNullOrWhiteSpace(code))
                    return Results.BadRequest("Missing ?code");

                await svc.ExchangeCodeForTokensAsync(code, ct);   // <-- change is here
                return Results.Text("Sage ID auth complete. You can close this tab.");
            })
            .WithName("Auth_Callback_Minimal")
            .ExcludeFromDescription();
        }
    }
}