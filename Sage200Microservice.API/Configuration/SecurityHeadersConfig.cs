namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Configuration options for security headers
    /// </summary>
    public class SecurityHeadersOptions
    {
        /// <summary>
        /// Gets or sets whether security headers are enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the Content-Security-Policy header value
        /// </summary>
        public string ContentSecurityPolicy { get; set; } = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'";

        /// <summary>
        /// Gets or sets the X-Frame-Options header value
        /// </summary>
        public string XFrameOptions { get; set; } = "DENY";

        /// <summary>
        /// Gets or sets the X-Content-Type-Options header value
        /// </summary>
        public string XContentTypeOptions { get; set; } = "nosniff";

        /// <summary>
        /// Gets or sets the Referrer-Policy header value
        /// </summary>
        public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

        /// <summary>
        /// Gets or sets the X-XSS-Protection header value
        /// </summary>
        public string XXssProtection { get; set; } = "1; mode=block";

        /// <summary>
        /// Gets or sets the Strict-Transport-Security header value
        /// </summary>
        public string StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";

        /// <summary>
        /// Gets or sets whether to enable HSTS preload
        /// </summary>
        public bool HstsPreload { get; set; } = false;

        /// <summary>
        /// Gets or sets the Permissions-Policy header value
        /// </summary>
        public string PermissionsPolicy { get; set; } = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

        /// <summary>
        /// Gets or sets whether to remove the Server header
        /// </summary>
        public bool RemoveServerHeader { get; set; } = true;

        /// <summary>
        /// Gets or sets the endpoints to exclude from security headers
        /// </summary>
        public List<string> ExcludedEndpoints { get; set; } = new List<string>();
    }

    /// <summary>
    /// Middleware for adding security headers to HTTP responses
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly SecurityHeadersOptions _options;

        public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _options = new SecurityHeadersOptions();
            configuration.GetSection("SecurityHeaders").Bind(_options);
        }

        public async Task Invoke(HttpContext context)
        {
            if (_options.Enabled && !IsExcludedPath(context.Request.Path))
            {
                // Add security headers
                var headers = context.Response.Headers;

                // Content-Security-Policy
                if (!string.IsNullOrEmpty(_options.ContentSecurityPolicy))
                {
                    headers["Content-Security-Policy"] = _options.ContentSecurityPolicy;
                }

                // X-Frame-Options
                if (!string.IsNullOrEmpty(_options.XFrameOptions))
                {
                    headers["X-Frame-Options"] = _options.XFrameOptions;
                }

                // X-Content-Type-Options
                if (!string.IsNullOrEmpty(_options.XContentTypeOptions))
                {
                    headers["X-Content-Type-Options"] = _options.XContentTypeOptions;
                }

                // Referrer-Policy
                if (!string.IsNullOrEmpty(_options.ReferrerPolicy))
                {
                    headers["Referrer-Policy"] = _options.ReferrerPolicy;
                }

                // X-XSS-Protection
                if (!string.IsNullOrEmpty(_options.XXssProtection))
                {
                    headers["X-XSS-Protection"] = _options.XXssProtection;
                }

                // Strict-Transport-Security
                if (!string.IsNullOrEmpty(_options.StrictTransportSecurity))
                {
                    var hstsValue = _options.StrictTransportSecurity;
                    if (_options.HstsPreload && !hstsValue.Contains("preload"))
                    {
                        hstsValue += "; preload";
                    }
                    headers["Strict-Transport-Security"] = hstsValue;
                }

                // Permissions-Policy
                if (!string.IsNullOrEmpty(_options.PermissionsPolicy))
                {
                    headers["Permissions-Policy"] = _options.PermissionsPolicy;
                }

                // Remove Server header
                if (_options.RemoveServerHeader)
                {
                    headers.Remove("Server");
                }
            }

            await _next(context);
        }

        private bool IsExcludedPath(PathString path)
        {
            return _options.ExcludedEndpoints.Any(endpoint =>
                path.StartsWithSegments(endpoint, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Extension methods for configuring security headers
    /// </summary>
    public static class SecurityHeadersConfig
    {
        /// <summary>
        /// Adds security headers services to the service collection
        /// </summary>
        public static IServiceCollection AddSecurityHeaders(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SecurityHeadersOptions>(configuration.GetSection("SecurityHeaders"));
            return services;
        }

        /// <summary>
        /// Uses security headers middleware in the application pipeline
        /// </summary>
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}