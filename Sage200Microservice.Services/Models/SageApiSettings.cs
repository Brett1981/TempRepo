using System;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Strongly-typed settings for Sage 200 (UKI) Professional (via Sage ID).
    /// Contract: <see cref="BaseUrl"/> MUST already include the version segment (e.g., .../v1/)
    /// and MUST end with a trailing slash. Do NOT append version segments in code.
    /// </summary>
    public sealed class SageApiSettings
    {
        /// <summary>
        /// Absolute base URL that already includes '/v1/' and ends with '/'.
        /// Example: https://api.columbus.sage.com/uk/sage200extra/accounts/v1/
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>OAuth2 client id (from Sage ID / Entra app registration).</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>OAuth2 client secret (store in user-secrets or environment variables).</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>OAuth2 token endpoint, e.g., https://id.sage.com/oauth/token</summary>
        public string TokenEndpoint { get; set; } = string.Empty;

        /// <summary>OAuth2 authorize endpoint, e.g., https://id.sage.com/authorize</summary>
        public string AuthorizationEndpoint { get; set; } = string.Empty;

        /// <summary>Redirect URI registered with Sage, e.g., https://localhost:7003/auth/callback</summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// OAuth2 audience / resource for which tokens are requested.
        /// </summary>
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Example: "openid offline_access profile"
        /// </summary>
        public string? Scopes { get; set; }

        public string SiteHeaderName { get; set; } = "X-Site";
        public string CompanyHeaderName { get; set; } = "X-Company";
        /// <summary>Default X-Site header value (optional; override per request when needed).</summary>
        public string? SiteId { get; set; }

        /// <summary>Default X-Company header value (optional; override per request when needed).</summary>
        public string? CompanyId { get; set; }

        /// <summary>OAuth2 revocation endpoint, e.g., https://id.sage.com/oauth/revoke (optional)</summary>
        public string? RevocationEndpoint { get; set; }

        // ---------------- Token Maintenance / Proactive Refresh ----------------
        public int? ProactiveRefreshWindowSeconds { get; set; } = 300; // refresh ≥5 min before expiry
        public int? MaintenanceMinimumDelaySeconds { get; set; } = 30; // clamp lowest sleep
        public int? KeepAliveMinutes { get; set; } = 60;               // refresh at least hourly when idle

        /// <summary>
        /// Validate required fields and normalize BaseUrl. Throws on invalid configuration.
        /// </summary>
        public void ValidateAndNormalize()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new InvalidOperationException("SageApi:BaseUrl is required.");

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException("SageApi:BaseUrl must be an absolute URL.");

            // Ensure trailing slash
            if (!BaseUrl.EndsWith("/"))
                BaseUrl += "/";

            // Require version segment present (e.g., .../v1/)
            var normalized = BaseUrl.ToLowerInvariant();
            if (!normalized.Contains("/v1/"))
                throw new InvalidOperationException(
                    "SageApi:BaseUrl MUST include the API version segment (e.g., .../v1/). " +
                    "Do NOT append version segments in code; fix configuration instead."
                );

            // Auth requirements
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("SageApi:ClientId is required.");
            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new InvalidOperationException("SageApi:ClientSecret is required.");
            if (string.IsNullOrWhiteSpace(TokenEndpoint))
                throw new InvalidOperationException("SageApi:TokenEndpoint is required.");
            if (string.IsNullOrWhiteSpace(AuthorizationEndpoint))
                throw new InvalidOperationException("SageApi:AuthorizationEndpoint is required.");
            if (string.IsNullOrWhiteSpace(RedirectUri))
                throw new InvalidOperationException("SageApi:RedirectUri is required.");
            if (string.IsNullOrWhiteSpace(Audience))
                throw new InvalidOperationException("SageApi:Audience is required.");
        }
    }
}
