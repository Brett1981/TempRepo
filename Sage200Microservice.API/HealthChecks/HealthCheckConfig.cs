using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sage200Microservice.Data;
using System.Net.Mime;
using System.Text.Json;

namespace Sage200Microservice.API.HealthChecks
{
    /// <summary>
    /// Configuration for health checks
    /// </summary>
    public static class HealthCheckConfig
    {
        /// <summary>
        /// Adds health checks to the service collection
        /// </summary>
        public static IServiceCollection AddHealthChecksConfig(this IServiceCollection services, IConfiguration cfg)
        {
            var hc = services.AddHealthChecks();

            // DB (readiness/core)
            hc.AddDbContextCheck<ApplicationContext>(
                name: "database",                                   // keep single, lower-case name
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "database", "sql", "core", "ready" });

            // Sage API (readiness/external)
            hc.AddCheck<SageApiHealthCheck>(
                name: "sage-api",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "api", "external", "core", "ready" });

            // Process memory (system)
            hc.AddProcessAllocatedMemoryHealthCheck(
                maximumMegabytesAllocated: 1024,
                name: "process-memory",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "memory", "system" });

            // Disk space (system) – pick root per OS
            var root = OperatingSystem.IsWindows() ? "C:\\" : "/";
            hc.AddDiskStorageHealthCheck(
                setup => setup.AddDrive(root, minimumFreeMegabytes: 1024),
                name: "disk-storage",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "disk", "system" });

            var localBase = cfg["Hosting:LocalHttpsUrl"] ?? "https://localhost:7003";
            services.AddHealthChecksUI(setup =>
            {
                setup.SetEvaluationTimeInSeconds(60);
                setup.MaximumHistoryEntriesPerEndpoint(50);
                setup.SetApiMaxActiveRequests(1);
                setup.AddHealthCheckEndpoint("Sage200Microservice API", $"{localBase.TrimEnd('/')}/health");
            })
            .AddInMemoryStorage();

            return services;
        }

        /// <summary>
        /// Configures health checks middleware
        /// </summary>
        public static IApplicationBuilder UseHealthChecksConfig(this IApplicationBuilder app)
        {
            // All checks
            app.UseHealthChecks("/health", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = WriteHealthCheckResponse
            });

            // UI
            app.UseHealthChecksUI(options =>
            {
                options.UIPath = "/health-ui";
                options.ApiPath = "/health-api";
            });

            // Subsets
            app.UseHealthChecks("/health/core", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("core"),
                ResponseWriter = WriteHealthCheckResponse
            });

            app.UseHealthChecks("/health/system", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("system"),
                ResponseWriter = WriteHealthCheckResponse
            });

            app.UseHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = WriteHealthCheckResponse
            });

            app.UseHealthChecks("/health/live", new HealthCheckOptions
            {
                // Liveness excludes readiness-only checks if you wish
                Predicate = check => !check.Tags.Contains("ready"),
                ResponseWriter = WriteHealthCheckResponse
            });

            return app;
        }

        /// <summary>
        /// Writes the health check response in JSON format
        /// </summary>
        private static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = MediaTypeNames.Application.Json;

            var response = new
            {
                Status = report.Status.ToString(),
                Duration = report.TotalDuration,
                Timestamp = DateTime.UtcNow,
                Checks = report.Entries.Select(entry => new
                {
                    Name = entry.Key,
                    Status = entry.Value.Status.ToString(),
                    Duration = entry.Value.Duration,
                    Description = entry.Value.Description,
                    Tags = entry.Value.Tags,
                    Data = entry.Value.Data
                })
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }
    }
}