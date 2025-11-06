using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Logging;

namespace Sage200Microservice.API.HealthChecks
{
    /// <summary>
    /// Writes all endpoints that match GET /auth/login to logs so you can see where the duplicate is coming from.
    /// </summary>
    public static class EndpointDiag
    {
        public static void LogAuthLoginDuplicates(this WebApplication app, ILogger logger)
        {
            var dataSource = app.Services.GetRequiredService<EndpointDataSource>();

            var matches = dataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Where(e =>
                {
                    var method = e.Metadata.OfType<HttpMethodMetadata>()
                                           .FirstOrDefault()?.HttpMethods?.Contains("GET") == true;
                    var path = string.Equals(e.RoutePattern.RawText, "/auth/login", StringComparison.OrdinalIgnoreCase);
                    return method && path;
                })
                .Select(e => e.DisplayName)
                .ToList();

            if (matches.Count > 1)
            {
                logger.LogWarning("Duplicate mappings for GET /auth/login detected:");
                foreach (var m in matches)
                    logger.LogWarning(" - {Endpoint}", m);
            }
            else
            {
                logger.LogInformation("No duplicate mappings for GET /auth/login.");
            }
        }

        public static void LogDuplicateRoutes(this WebApplication app, ILogger logger)
        {
            var ds = app.Services.GetRequiredService<EndpointDataSource>();

            // Project endpoints to (Method, Path, DisplayName)
            var endpoints = ds.Endpoints
                .OfType<RouteEndpoint>()
                .Select(ep =>
                {
                    var methods = ep.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods
                                  ?? new[] { "ANY" };
                    var path = "/" + (ep.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                    return methods.Select(m => new { Method = m.ToUpperInvariant(), Path = path, ep.DisplayName });
                })
                .SelectMany(x => x)
                .ToList();

            // Group by (method, path) — find duplicates
            var dups = endpoints
                .GroupBy(e => new { e.Method, e.Path })
                .Where(g => g.Count() > 1)
                .ToList();

            if (!dups.Any())
            {
                logger.LogInformation("No duplicate routes detected.");
                return;
            }

            foreach (var g in dups)
            {
                logger.LogWarning("DUPLICATE: {Method} {Path} has {Count} mappings",
                    g.Key.Method, g.Key.Path, g.Count());

                foreach (var ep in g)
                    logger.LogWarning(" - {DisplayName}", ep.DisplayName);
            }
        }
    }
}
