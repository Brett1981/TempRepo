using AspNetCoreRateLimit;
using Microsoft.Extensions.Options;

namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Adds the API-key resolver to the standard RateLimitConfiguration.
    /// </summary>
    public class ApiKeyRateLimitConfiguration : RateLimitConfiguration
    {
        public ApiKeyRateLimitConfiguration(
                    IOptions<IpRateLimitOptions> ipOptions,
                    IOptions<ClientRateLimitOptions> clientOptions,
                    ApiKeyClientResolveContributor apiKeyResolver)
                    : base(ipOptions, clientOptions) // your package version: 2-arg base ctor
        {
            // Ensure our resolver runs first
            ClientResolvers.Insert(0, apiKeyResolver);
        }
    }
}