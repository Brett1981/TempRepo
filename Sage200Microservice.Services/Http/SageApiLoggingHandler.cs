using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Logging;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Logs every outbound Sage API call into ApiLogs and emits a paired AuditLogs record.
    /// - ApiLogs: full request/response payloads (encryption handled inside DbLogWriter).
    /// - AuditLogs: summary record with non-sensitive details and correlation.
    /// </summary>
    public sealed class SageApiLoggingHandler : DelegatingHandler
    {
        private const string CorrelationHeader = "X-Correlation-Id";
        private readonly ILogger<SageApiLoggingHandler> _logger;
        private readonly IDbLogWriter _dbLogWriter;

        public SageApiLoggingHandler(ILogger<SageApiLoggingHandler> logger, IDbLogWriter dbLogWriter)
        {
            _logger = logger;
            _dbLogWriter = dbLogWriter;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            // Basic context
            var endpoint = request.RequestUri?.PathAndQuery ?? request.RequestUri?.ToString() ?? "";
            var method = request.Method.Method;

            // Correlation
            string correlationId = TryGetHeader(request, CorrelationHeader) ?? Guid.NewGuid().ToString("N");

            // Buffer request body (if any) for logging, then restore content so pipeline can still send it
            string? reqBody = null;
            MediaTypeHeaderValue? reqContentType = request.Content?.Headers.ContentType;
            try
            {
                if (request.Content != null)
                {
                    reqBody = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    // Recreate content
                    var media = reqContentType?.MediaType ?? "application/json";
                    request.Content = new StringContent(reqBody, Encoding.UTF8, media);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to capture request body for {Method} {Endpoint}", method, endpoint);
            }

            HttpResponseMessage? resp = null;
            string? respBody = null;
            int statusCode = 0;
            MediaTypeHeaderValue? respContentType = null;

            try
            {
                resp = await base.SendAsync(request, ct).ConfigureAwait(false);
                statusCode = (int)resp.StatusCode;

                if (resp.Content != null)
                {
                    respContentType = resp.Content.Headers.ContentType;
                    respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    // Put back the response content so callers down the pipeline can read again
                    var media = respContentType?.MediaType ?? "application/json";
                    resp.Content = new StringContent(respBody, Encoding.UTF8, media);
                }

                return resp;
            }
            catch (Exception ex)
            {
                // We still log API & Audit records in finally
                statusCode = 0;
                respBody = JsonSerializer.Serialize(new { error = ex.Message });
                _logger.LogWarning(ex, "Sage call failed {Method} {Endpoint}", method, endpoint);

                var authHdr = resp?.Headers.WwwAuthenticate?.ToString();
                _logger.LogWarning("Upstream error creating SalesInvoice: status={Status} auth={Auth} body={BodyPreview}",
                    (int)resp?.StatusCode, authHdr, respBody ?? "");
                throw;
            }
            finally
            {
                sw.Stop();

                // ---------- ApiLogs (full payloads; encryption handled by DbLogWriter) ----------
                var api = new ApiLogRecord
                {
                    Endpoint = endpoint,
                    RequestMethod = method,
                    RequestPayloadEncrypted = reqBody,
                    ResponsePayloadEncrypted = respBody,
                    HttpStatusCode = statusCode,
                    CallerId = null,                     // Populate if you pass a caller id header
                    ApiType = "Sage200.SOP"             // Adjust if you split ApiType by feature
                };

                try
                {
                    await _dbLogWriter.WriteApiAsync(api, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "ApiLogs write canceled for {Method} {Endpoint}", method, endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed writing ApiLogs for {Method} {Endpoint}", method, endpoint);
                }

                // ---------- AuditLogs (compact, non-sensitive summary) ----------
                var (statusInt, severityInt) = MapStatusAndSeverity(statusCode);
                var resource = InferResource(endpoint); // e.g., "Sage200.SOP.Order" for /sop_orders
                var action = InferAction(method, endpoint);

                // Build a small details object (non-sensitive)
                var auditDetails = new
                {
                    correlationId,
                    status = statusCode,
                    durationMs = sw.ElapsedMilliseconds,
                    endpoint,
                    method,
                    requestContentType = reqContentType?.MediaType,
                    responseContentType = respContentType?.MediaType
                };
                var detailsJson = JsonSerializer.Serialize(auditDetails);

                var audit = new AuditLogRecord
                {
                    // Event axes — ints per DB schema
                    EventType = 0,                      // e.g., 0 = API
                    Category = 1,                       // e.g., 1 = Sage200
                    Severity = severityInt,             // 0=Info,1=Warn,2=Error
                    Status = statusInt,                 // 0=Success,1=ClientError,2=ServerError

                    // Required non-nullables in schema
                    IpAddress = "0.0.0.0",              // No HttpContext here; use placeholder
                    Resource = resource,
                    Action = action,
                    Description = $"Outbound {method} {endpoint}",
                    Details = detailsJson,
                    CorrelationId = correlationId,
                    HttpMethod = method,
                    UrlPath = endpoint,
                    UserAgent = "Sage200Microservice/HttpClient",

                    // Optionals
                    HttpStatusCode = statusCode == 0 ? null : statusCode,
                    DurationMs = sw.ElapsedMilliseconds
                };

                try
                {
                    await _dbLogWriter.WriteAuditAsync(audit, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "AuditLogs write canceled for {Method} {Endpoint}", method, endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed writing AuditLogs for {Method} {Endpoint}", method, endpoint);
                }
            }
        }

        private static string? TryGetHeader(HttpRequestMessage req, string name)
        {
            if (req.Headers.TryGetValues(name, out var vals))
                return string.Join(",", vals);
            return null;
        }

        private static (int status, int severity) MapStatusAndSeverity(int httpStatus)
        {
            // Status: 0=Success, 1=ClientError, 2=ServerError
            // Severity: 0=Info, 1=Warning, 2=Error
            if (httpStatus >= 200 && httpStatus <= 399) return (0, 0);
            if (httpStatus >= 400 && httpStatus <= 499) return (1, 1);
            if (httpStatus >= 500 && httpStatus <= 599) return (2, 2);
            return (2, 2); // unknown/error path
        }

        private static string InferResource(string endpoint)
        {
            // Simple heuristic; extend as you add resources
            // /sop_orders or /sop_orders/{id}
            if (endpoint.IndexOf("/sop_orders", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Sage200.SOP.Order";

            return "Sage200.API";
        }

        private static string InferAction(string method, string endpoint)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                // If URL contains .../sop_orders/{id} → Read, else List
                var parts = endpoint.Split('?', 2)[0].TrimEnd('/').Split('/');
                return parts.Length > 0 && long.TryParse(parts[^1], out _)
                    ? "Read"
                    : "List";
            }
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) return "Create";
            if (string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)) return "Update";
            if (string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)) return "Update";
            if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)) return "Delete";
            return "Call";
        }
    }
}
