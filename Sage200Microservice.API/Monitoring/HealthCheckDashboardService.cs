using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text;
using System.Text.Json;

namespace Sage200Microservice.API.Monitoring
{
    public class HealthCheckDashboardOptions
    {
        public bool Enabled { get; set; } = true;
        public string EndpointPath { get; set; } = "/health-dashboard";
        public string ApiEndpointPath { get; set; } = "/health";
        public bool IncludeDetails { get; set; } = true;
        public bool IncludeErrorMessages { get; set; } = true;
        public bool IncludeExceptions { get; set; } = false;
        public int RefreshIntervalSeconds { get; set; } = 30;
        public bool AllowUnauthenticatedAccess { get; set; } = false;
        public List<string> AllowedRoles { get; set; } = new() { "Administrator", "Operations" };
    }

    public static class HealthCheckDashboardExtensions
    {
        /// <summary>
        /// Only bind options. DO NOT register health checks here to avoid duplicates.
        /// </summary>
        public static IServiceCollection AddHealthCheckDashboard(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<HealthCheckDashboardOptions>(configuration.GetSection("HealthCheckDashboard"));
            return services;
        }

        public static IApplicationBuilder UseHealthCheckDashboard(this IApplicationBuilder app, IConfiguration configuration)
        {
            var options = new HealthCheckDashboardOptions();
            configuration.GetSection("HealthCheckDashboard").Bind(options);

            if (!options.Enabled)
                return app;

            // API endpoint (re-uses existing registrations from HealthCheckConfig)
            app.UseHealthChecks(options.ApiEndpointPath, new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";

                    var response = new
                    {
                        status = report.Status.ToString(),
                        totalDuration = report.TotalDuration,
                        timestamp = DateTime.UtcNow,
                        entries = report.Entries.Select(e => new
                        {
                            name = e.Key,
                            status = e.Value.Status.ToString(),
                            duration = e.Value.Duration,
                            description = e.Value.Description,
                            tags = e.Value.Tags,
                            data = options.IncludeDetails ? e.Value.Data : null,
                            error = options.IncludeErrorMessages ? e.Value.Exception?.Message : null,
                            exception = options.IncludeExceptions ? e.Value.Exception : null
                        })
                    };

                    var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
                    await context.Response.WriteAsync(json);
                }
            });

            // HTML dashboard
            app.Map(options.EndpointPath, dashboardApp =>
            {
                if (!options.AllowUnauthenticatedAccess)
                {
                    dashboardApp.Use(async (context, next) =>
                    {
                        if (!context.User.Identity?.IsAuthenticated ?? true ||
                            !options.AllowedRoles.Any(r => context.User.IsInRole(r)))
                        {
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsync("Unauthorized");
                            return;
                        }
                        await next();
                    });
                }

                dashboardApp.Run(async context =>
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(GenerateHealthDashboardHtml(options));
                });
            });

            return app;
        }

        private static string GenerateHealthDashboardHtml(HealthCheckDashboardOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=&quot;en&quot;>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=&quot;UTF-8&quot;>");
            sb.AppendLine("  <meta name=&quot;viewport&quot; content=&quot;width=device-width, initial-scale=1.0&quot;>");
            sb.AppendLine("  <title>Health Check Dashboard</title>");
            sb.AppendLine("  <style>/* (styles unchanged) */</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <div class=&quot;container&quot;>");
            sb.AppendLine("    <h1>Health Check Dashboard</h1>");
            sb.AppendLine("    <div id=&quot;status-container&quot;></div>");
            sb.AppendLine("    <div class=&quot;refresh-info&quot;>");
            sb.AppendLine($"      Auto-refreshing every {options.RefreshIntervalSeconds} seconds. Last updated: <span id=&quot;last-updated&quot;>-</span>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <script>");
            sb.AppendLine("    function updateHealthStatus(){");
            sb.AppendLine($"      fetch('{options.ApiEndpointPath}')");
            sb.AppendLine("        .then(r => r.json())");
            sb.AppendLine("        .then(data => { /* (rendering unchanged) */ document.getElementById('last-updated').textContent = new Date().toLocaleString(); })");
            sb.AppendLine("        .catch(err => { console.error(err); document.getElementById('last-updated').textContent = new Date().toLocaleString(); });");
            sb.AppendLine("    }");
            sb.AppendLine("    updateHealthStatus();");
            sb.AppendLine($"    setInterval(updateHealthStatus, {options.RefreshIntervalSeconds * 1000});");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }
    }
}