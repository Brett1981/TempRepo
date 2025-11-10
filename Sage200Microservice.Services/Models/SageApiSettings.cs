using System;
using Microsoft.Extensions.Configuration;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Strongly-typed configuration for Sage API access and header policies.
    /// </summary>
    public sealed class SageApiSettings
    {
        public bool ForwardApiKeyToSage { get; set; } = false;
        /// <summary>Absolute base URL for Sage 200 API (must end with '/').</summary>
        public string BaseUrl { get; set; } = "https://api.columbus.sage.com/uk/sage200extra/accounts/v1/";

        /// <summary>OAuth2 client id.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>OAuth2 client secret.</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>Token endpoint URL.</summary>
        public string TokenEndpoint { get; set; } = "https://id.sage.com/oauth/token";

        /// <summary>Authorize endpoint URL.</summary>
        public string AuthorizationEndpoint { get; set; } = "https://id.sage.com/authorize";

        /// <summary>Redirect URI configured with the IdP.</summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>Space-separated scopes provided for the OAuth flow (e.g., "openid profile email offline_access").</summary>
        public string Scopes { get; set; } = "openid profile email offline_access";

        /// <summary>Primary resource/audience the token must target (e.g., "s200ukipd/sage200").</summary>
        public string Audience { get; set; } = "s200ukipd/sage200";

        /// <summary>Header name used to convey the Sage site id.</summary>
        public string SiteHeaderName { get; set; } = "X-Site";

        /// <summary>Header name used to convey the Sage company id.</summary>
        public string CompanyHeaderName { get; set; } = "X-Company";

        /// <summary>Header name used to convey the caller application API key.</summary>
        public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

        /// <summary>Default SiteId to inject when absent from inbound requests (Development/test convenience).</summary>
        public string? SiteId { get; set; }

        /// <summary>Default CompanyId to inject when absent from inbound requests (Development/test convenience).</summary>
        public string? CompanyId { get; set; }

        /// <summary>Default API key to use when running in Development and no X-Api-Key was provided.</summary>
        public string? DevelopmentDefaultApiKey { get; set; }

        /// <summary>Whether Development profile may auto-inject the default API key into outbound Sage requests.</summary>
        public bool AllowDevelopmentFallbackApiKey { get; set; } = true;

        /// <summary>Enable dev/test fault injection via headers (e.g., X-Fault).</summary>
        public bool EnableFaultInjection { get; set; } = false;

        /// <summary>
        /// Optional list of API keys that should be treated as Admins (for environments without JWT roles).
        /// If the inbound X-Api-Key matches one of these, we grant role "Admin" for the request scope.
        /// </summary>
        public string[] AdminApiKeys { get; set; } = Array.Empty<string>();

        /// <summary>Logging sub-config.</summary>
        public SageApiLoggingSettings Logging { get; set; } = new();

        /// <summary>
        /// Normalizes and validates key properties for safe use at runtime.
        /// (We add this to satisfy existing calls and avoid null/format issues.)
        /// </summary>
        public void ValidateAndNormalize()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new InvalidOperationException("SageApi:BaseUrl is required.");

            if (!BaseUrl.EndsWith("/", StringComparison.Ordinal))
                BaseUrl += "/";

            if (string.IsNullOrWhiteSpace(TokenEndpoint))
                throw new InvalidOperationException("SageApi:TokenEndpoint is required.");

            if (string.IsNullOrWhiteSpace(AuthorizationEndpoint))
                throw new InvalidOperationException("SageApi:AuthorizationEndpoint is required.");

            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("SageApi:ClientId is required.");

            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new InvalidOperationException("SageApi:ClientSecret is required.");
        }
    }

    /// <summary>Payload logging options for outbound Sage calls.</summary>
    public sealed class SageApiLoggingSettings
    {
        public bool Enabled { get; set; } = true;
        public bool IncludePayloads { get; set; } = true;
        public bool EncryptPayloads { get; set; } = true;
        public int MaxBodyBytes { get; set; } = 65536;
    }
}
