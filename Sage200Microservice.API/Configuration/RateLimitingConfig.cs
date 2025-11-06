using AspNetCoreRateLimit;

namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Configuration for rate limiting
    /// </summary>
    public static class RateLimitingConfig
    {
        /// <summary>
        /// Adds rate limiting services to the service collection
        /// </summary>
        /// <param name="services">      The service collection </param>
        /// <param name="configuration"> The configuration </param>
        /// <returns> The service collection </returns>
        public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            // Load rate limiting configuration from appsettings.json
            services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
            services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));
            services.Configure<ClientRateLimitOptions>(configuration.GetSection("ClientRateLimiting"));
            services.Configure<ClientRateLimitPolicies>(configuration.GetSection("ClientRateLimitPolicies"));

            // Register rate limiting services
            services.AddInMemoryRateLimiting();

            // Use our custom rate limit configuration provider
            services.AddSingleton<IRateLimitConfiguration, ApiKeyRateLimitConfiguration>();

            // Add memory cache for storing rate limit counters
            services.AddMemoryCache();

            return services;
        }

        /// <summary>
        /// Configures rate limiting middleware
        /// </summary>
        /// <param name="app"> The application builder </param>
        /// <returns> The application builder </returns>
        public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
        {
            // Use client rate limiting middleware
            app.UseClientRateLimiting();

            // Use IP rate limiting middleware
            app.UseIpRateLimiting();

            return app;
        }
    }
}