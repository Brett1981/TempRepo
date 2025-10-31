using OpenTelemetry.Trace;
using Sage200Microservice.API.Tracing;
using System.Diagnostics;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Middleware for handling distributed tracing
    /// </summary>
    public class TracingMiddleware
    {
        private readonly RequestDelegate _next;

        /// <summary>
        /// Initializes a new instance of the TracingMiddleware class
        /// </summary>
        /// <param name="next"> The next middleware in the pipeline </param>
        public TracingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// Invokes the middleware
        /// </summary>
        /// <param name="context"> The HTTP context </param>
        /// <returns> A task representing the asynchronous operation </returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip tracing for certain paths like health checks
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await _next(context);
                return;
            }

            // Create a new activity for the request if one doesn't already exist
            using var activity = ActivitySourceProvider.ApiSource.StartActivity(
                $"{context.Request.Method} {context.Request.Path}",
                ActivityKind.Server);

            if (activity != null)
            {
                // Add common tags to the activity
                activity.SetTag("http.method", context.Request.Method);
                activity.SetTag("http.url", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
                activity.SetTag("http.host", context.Request.Host.ToString());
                activity.SetTag("http.path", context.Request.Path.ToString());
                activity.SetTag("http.query_string", context.Request.QueryString.ToString());

                // Add user agent if available
                if (context.Request.Headers.TryGetValue("User-Agent", out var userAgent))
                {
                    activity.SetTag("http.user_agent", userAgent.ToString());
                }

                // Add client IP address
                activity.SetTag("http.client_ip", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                // Add custom attributes
                if (context.Request.Headers.TryGetValue("X-Request-ID", out var requestId))
                {
                    activity.SetTag("request.id", requestId.ToString());
                }

                // Add API key information if available (masked for security)
                if (context.Request.Headers.TryGetValue("X-API-Key", out var _))
                {
                    activity.SetTag("auth.type", "api_key");
                }
            }

            try
            {
                // Call the next middleware in the pipeline
                await _next(context);

                // Add response information to the activity
                if (activity != null)
                {
                    activity.SetTag("http.status_code", context.Response.StatusCode);
                    activity.SetStatus(context.Response.StatusCode < 400 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

                    // Add content type if available
                    if (context.Response.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        activity.SetTag("http.response.content_type", contentType.ToString());
                    }

                    // Add content length if available
                    if (context.Response.Headers.TryGetValue("Content-Length", out var contentLength))
                    {
                        activity.SetTag("http.response.content_length", contentLength.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // Record the exception in the activity
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity.RecordException(ex);
                }

                // Re-throw the exception to be handled by the global exception handler
                throw;
            }
        }
    }

    /// <summary>
    /// Extension methods for the TracingMiddleware
    /// </summary>
    public static class TracingMiddlewareExtensions
    {
        /// <summary>
        /// Adds the tracing middleware to the application pipeline
        /// </summary>
        /// <param name="app"> The application builder </param>
        /// <returns> The application builder </returns>
        public static IApplicationBuilder UseTracing(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TracingMiddleware>();
        }
    }
}