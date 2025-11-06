using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;

namespace Sage200Microservice.API.Logging
{
    /// <summary>
    /// Configuration for Serilog
    /// </summary>
    public static class SerilogConfig
    {
        /// <summary>
        /// Configures Serilog for the application
        /// </summary>
        /// <param name="builder"> The host builder </param>
        /// <returns> The host builder </returns>
        public static IHostBuilder UseSerilogLogging(this IHostBuilder builder)
        {
            return builder.UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithProperty("Application", "Sage200Microservice")
                    .Enrich.WithExceptionDetails()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .WriteTo.Console(new CompactJsonFormatter())
                    .WriteTo.File(new CompactJsonFormatter(),
                        path: "logs/sage200microservice-.log",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30);

                // Add additional sinks based on configuration
                var config = context.Configuration;

                // Add Seq logging if configured
                var seqServerUrl = config["Serilog:SeqServerUrl"];
                if (!string.IsNullOrEmpty(seqServerUrl))
                {
                    configuration.WriteTo.Seq(seqServerUrl);
                }
            });
        }

        /// <summary>
        /// Adds Serilog services to the service collection
        /// </summary>
        /// <param name="services"> The service collection </param>
        /// <returns> The service collection </returns>
        public static IServiceCollection AddSerilogServices(this IServiceCollection services)
        {
            return services.AddSingleton(Log.Logger);
        }
    }
}