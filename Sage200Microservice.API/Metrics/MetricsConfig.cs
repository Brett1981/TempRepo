using Prometheus;

namespace Sage200Microservice.API.Metrics
{
    /// <summary>
    /// Configuration for metrics collection
    /// </summary>
    public static class MetricsConfig
    {
        /// <summary>
        /// Adds metrics services to the service collection
        /// </summary>
        /// <param name="services"> The service collection </param>
        /// <returns> The service collection </returns>
        public static IServiceCollection AddMetrics(this IServiceCollection services)
        {
            // Add Prometheus system metrics collection
            services.AddSingleton<ICollectorRegistry, CollectorRegistry>();
            services.AddHostedService<SystemMetricsCollector>();

            // Register custom metrics
            services.AddSingleton<ApiMetrics>();
            services.AddSingleton<DatabaseMetrics>();
            services.AddSingleton<SageApiMetrics>();
            services.AddSingleton<BackgroundServiceMetrics>();

            return services;
        }

        /// <summary>
        /// Configures metrics middleware
        /// </summary>
        /// <param name="app"> The application builder </param>
        /// <returns> The application builder </returns>
        public static IApplicationBuilder UseMetrics(this IApplicationBuilder app)
        {
            // Use Prometheus metrics middleware
            app.UseHttpMetrics(options =>
            {
                // Configure HTTP metrics
                options.AddCustomLabel("host", context => context.Request.Host.Host);

                // Track request duration by endpoint
                options.RequestDuration.Enabled = true;

                // Track in-progress requests
                options.InProgress.Enabled = true;

                // Track request size
                options.RequestCount.Enabled = true;
            });

            // Expose Prometheus metrics at /metrics endpoint
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
            });

            return app;
        }
    }

    /// <summary>
    /// Background service to collect system metrics
    /// </summary>
    public class SystemMetricsCollector : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Collect system metrics periodically
            while (!stoppingToken.IsCancellationRequested)
            {
                // Example: Collect CPU, memory, etc. Add your system metrics collection logic here
                Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).Wait();
            }
            return Task.CompletedTask;
        }
    }
}