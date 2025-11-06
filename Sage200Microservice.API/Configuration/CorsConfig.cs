namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Configuration options for CORS policy
    /// </summary>
    public class CorsOptions
    {
        /// <summary>
        /// Gets or sets whether CORS is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the allowed origins
        /// </summary>
        public List<string> AllowedOrigins { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the allowed HTTP methods
        /// </summary>
        public List<string> AllowedMethods { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the allowed HTTP headers
        /// </summary>
        public List<string> AllowedHeaders { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the exposed headers
        /// </summary>
        public List<string> ExposedHeaders { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets whether credentials are allowed
        /// </summary>
        public bool AllowCredentials { get; set; }

        /// <summary>
        /// Gets or sets the preflightMaxAge in seconds
        /// </summary>
        public int PreflightMaxAgeInSeconds { get; set; } = 600; // 10 minutes
    }

    /// <summary>
    /// Extension methods for configuring CORS
    /// </summary>
    public static class CorsConfig
    {
        private const string DefaultCorsPolicyName = "DefaultCorsPolicy";

        /// <summary>
        /// Adds CORS services to the service collection
        /// </summary>
        public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
        {
            var corsOptions = new CorsOptions();
            configuration.GetSection("Cors").Bind(corsOptions);

            if (!corsOptions.Enabled)
            {
                return services;
            }

            services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, builder =>
                {
                    // Configure origins
                    if (corsOptions.AllowedOrigins.Any())
                    {
                        if (corsOptions.AllowedOrigins.Contains("*"))
                        {
                            builder.AllowAnyOrigin();
                        }
                        else
                        {
                            builder.WithOrigins(corsOptions.AllowedOrigins.ToArray());
                        }
                    }
                    else
                    {
                        builder.AllowAnyOrigin();
                    }

                    // Configure methods
                    if (corsOptions.AllowedMethods.Any())
                    {
                        builder.WithMethods(corsOptions.AllowedMethods.ToArray());
                    }
                    else
                    {
                        builder.AllowAnyMethod();
                    }

                    // Configure headers
                    if (corsOptions.AllowedHeaders.Any())
                    {
                        builder.WithHeaders(corsOptions.AllowedHeaders.ToArray());
                    }
                    else
                    {
                        builder.AllowAnyHeader();
                    }

                    // Configure exposed headers
                    if (corsOptions.ExposedHeaders.Any())
                    {
                        builder.WithExposedHeaders(corsOptions.ExposedHeaders.ToArray());
                    }

                    // Configure credentials
                    if (corsOptions.AllowCredentials)
                    {
                        builder.AllowCredentials();
                    }
                    else
                    {
                        builder.DisallowCredentials();
                    }

                    // Configure preflight cache duration
                    if (corsOptions.PreflightMaxAgeInSeconds > 0)
                    {
                        builder.SetPreflightMaxAge(TimeSpan.FromSeconds(corsOptions.PreflightMaxAgeInSeconds));
                    }
                });
            });

            return services;
        }

        /// <summary>
        /// Uses CORS middleware in the application pipeline
        /// </summary>
        public static IApplicationBuilder UseCorsPolicy(this IApplicationBuilder app, IConfiguration configuration)
        {
            var corsOptions = new CorsOptions();
            configuration.GetSection("Cors").Bind(corsOptions);

            if (corsOptions.Enabled)
            {
                app.UseCors(DefaultCorsPolicyName);
            }

            return app;
        }
    }
}