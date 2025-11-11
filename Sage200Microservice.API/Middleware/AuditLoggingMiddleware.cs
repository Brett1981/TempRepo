using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Middleware for audit logging of HTTP requests
    /// </summary>
    public class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditLoggingMiddleware> _logger;
        private readonly AuditLogSettings _settings;

        public AuditLoggingMiddleware(
            RequestDelegate next,
            ILogger<AuditLoggingMiddleware> logger,
            IOptions<AuditLogSettings> options)
        {
            _next = next;
            _logger = logger;
            _settings = options.Value;
        }

        public async Task InvokeAsync(HttpContext context, IAuditLogService auditLogService)
        {
            // Skip if audit logging is disabled or HTTP request logging is disabled
            if (!_settings.Enabled || !_settings.LogHttpRequests)
            {
                await _next(context);
                return;
            }

            // NEW: Skip when endpoint is explicitly marked with [SkipAudit]
            var ep = context.GetEndpoint();
            if (ep?.Metadata?.GetMetadata<SkipAuditAttribute>() is not null)
            {
                await _next(context);
                return;
            }

            // Skip for excluded endpoints
            var endpoint = context.Request.Path.Value ?? string.Empty;
            foreach (var excludedEndpoint in _settings.ExcludedEndpoints)
            {
                if (endpoint.StartsWith(excludedEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
            }

            // Determine whether this endpoint is "sensitive"
            bool isSensitiveEndpoint = false;
            foreach (var sensitiveEndpoint in _settings.SensitiveEndpoints)
            {
                if (endpoint.StartsWith(sensitiveEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    isSensitiveEndpoint = true;
                    break;
                }
            }

            // If configured to log only sensitive endpoints, skip non-sensitive ones
            if (_settings.LogOnlySensitiveEndpoints && !isSensitiveEndpoint)
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            var httpMethod = context.Request.Method;
            var urlPath = context.Request.Path.Value;
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var ipAddress = context.Connection.RemoteIpAddress?.ToString();
            var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
                context.Request.Headers["X-Correlation-ID"] = correlationId;
            }

            // Get user ID and client ID from claims or headers
            string? userId = null;
            string? clientId = null;

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                userId = context.User.Identity?.Name;
            }

            // Prefer friendly client name set by API-key middleware; fallback to the header value
            clientId = context.Items.TryGetValue("ClientName", out var clientObj)
                ? clientObj?.ToString()
                : (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) ? apiKey.ToString() : null);

            // --- Request body (text-only, bounded) ---
            string? requestBody = null;
            var requestContentLength = context.Request.ContentLength ?? 0;
            var isTextRequest = IsTextualContent(context.Request.ContentType);

            if (isTextRequest && (requestContentLength == 0 || requestContentLength <= _settings.MaxDetailsSize))
            {
                try
                {
                    context.Request.EnableBuffering();

                    var originalPos = context.Request.Body.CanSeek ? context.Request.Body.Position : 0;
                    using var reader = new StreamReader(
                        context.Request.Body,
                        encoding: Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: false,
                        leaveOpen: true);

                    requestBody = await reader.ReadToEndAsync();

                    if (context.Request.Body.CanSeek)
                        context.Request.Body.Position = originalPos;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading request body for audit logging");
                }
            }

            var originalResponseBody = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                await _next(context);

                stopwatch.Stop();

                var statusCode = context.Response.StatusCode;
                var durationMs = stopwatch.ElapsedMilliseconds;

                // --- Response body (text-only, bounded, and "nice-to-have": only for 4xx/5xx or
                //     sensitive endpoints) ---
                string? responseBody = null;
                var shouldCaptureResponse = ShouldCaptureResponseBody(statusCode, isSensitiveEndpoint);
                var isTextResponse = IsTextualContent(context.Response.ContentType);

                if (shouldCaptureResponse && isTextResponse && responseBodyStream.Length > 0 && responseBodyStream.Length <= _settings.MaxDetailsSize)
                {
                    try
                    {
                        responseBodyStream.Position = 0;
                        using var reader = new StreamReader(
                            responseBodyStream,
                            encoding: Encoding.UTF8,
                            detectEncodingFromByteOrderMarks: false,
                            leaveOpen: true);
                        responseBody = await reader.ReadToEndAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading response body for audit logging");
                    }
                }

                responseBodyStream.Position = 0;
                await responseBodyStream.CopyToAsync(originalResponseBody);

                var description = $"{httpMethod} {urlPath} - {statusCode}";

                var details = new
                {
                    Request = new
                    {
                        Headers = MaskSensitiveHeaders(context.Request.Headers),
                        Body = MaskSensitiveData(requestBody)
                    },
                    Response = new
                    {
                        Headers = context.Response.Headers,
                        Body = MaskSensitiveData(responseBody)
                    }
                };

                await auditLogService.LogHttpRequestAsync(
                    userId,
                    clientId,
                    ipAddress,
                    httpMethod,
                    urlPath,
                    statusCode,
                    durationMs,
                    userAgent,
                    description,
                    details,
                    correlationId);
            }
            catch (Exception ex)
            {
                // Lower severity for upstream flakiness that will be handled by global handler as 424
                var isUpstreamFlaky = ex is TaskCanceledException ||
                                      (ex is HttpRequestException hre && (hre.StatusCode is null || (int)hre.StatusCode >= 500));

                if (isUpstreamFlaky &&
                    (urlPath?.StartsWith("/api/Customers", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    _logger.LogWarning(ex, "Upstream flakiness during {Method} {Path}; will be handled/degraded.", httpMethod, urlPath);
                }
                else
                {
                    _logger.LogError(ex, "Error in audit logging middleware");
                }

                // Ensure the response body is copied to the original stream
                responseBodyStream.Position = 0;
                await responseBodyStream.CopyToAsync(originalResponseBody);

                // Re-throw the exception to be handled by the global exception handler
                throw;
            }
            finally
            {
                context.Response.Body = originalResponseBody;
            }
        }

        private static bool ShouldCaptureResponseBody(int statusCode, bool isSensitiveEndpoint)
            => statusCode >= 400 || isSensitiveEndpoint;

        private static bool IsTextualContent(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;

            var semi = contentType.IndexOf(';');
            if (semi >= 0) contentType = contentType[..semi];

            contentType = contentType.Trim().ToLowerInvariant();

            if (contentType.StartsWith("text/")) return true;

            return contentType is "application/json"
                or "application/problem+json"
                or "application/xml"
                or "application/xhtml+xml"
                or "application/x-www-form-urlencoded";
        }

        private object MaskSensitiveHeaders(IHeaderDictionary headers)
        {
            if (!_settings.MaskSensitiveData)
            {
                return headers;
            }

            var maskedHeaders = new Dictionary<string, string>();

            foreach (var header in headers)
            {
                var key = header.Key;
                var value = header.Value.ToString();

                bool isSensitive = false;
                foreach (var sensitiveField in _settings.SensitiveFields)
                {
                    if (key.Contains(sensitiveField, StringComparison.OrdinalIgnoreCase))
                    {
                        isSensitive = true;
                        break;
                    }
                }

                if (isSensitive)
                {
                    maskedHeaders[key] = "********";
                }
                else
                {
                    maskedHeaders[key] = value;
                }
            }

            return maskedHeaders;
        }

        private string? MaskSensitiveData(string? data)
        {
            if (string.IsNullOrEmpty(data) || !_settings.MaskSensitiveData)
            {
                return data;
            }

            var masked = data;

            foreach (var field in _settings.SensitiveFields)
            {
                var f = Regex.Escape(field);

                var jsonStringPattern = $"(\"{f}\"\\s*:\\s*)\"([^\"]*)\"";
                masked = Regex.Replace(masked, jsonStringPattern, $"$1\"********\"", RegexOptions.IgnoreCase);

                var jsonBarePattern = $"(\"{f}\"\\s*:\\s*)([^\",\\s}}\\]]+)";
                masked = Regex.Replace(masked, jsonBarePattern, $"$1\"********\"", RegexOptions.IgnoreCase);

                var formPattern = $"(?<=^|[&]){f}=([^&\\r\\n]*)";
                masked = Regex.Replace(masked, formPattern, $"{field}=********", RegexOptions.IgnoreCase);

                var xmlPattern = $"(<{f}>)(.*?)(</{f}>)";
                masked = Regex.Replace(masked, xmlPattern, $"$1********$3", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            return masked;
        }
    }

    public static class AuditLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app)
        {
            return app.UseMiddleware<AuditLoggingMiddleware>();
        }
    }
}