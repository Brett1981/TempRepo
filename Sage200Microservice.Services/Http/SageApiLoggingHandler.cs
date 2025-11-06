using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Logging;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Http
{
    /// <summary>
    /// Logs every outbound Sage API call into ApiLogs and emits a paired AuditLogs record.
    /// Enhancements:
    ///  - Header snapshot (X-Site, X-Company, X-Api-Key present?, Authorization present?) with safe redaction.
    ///  - Honors SageApi:Logging settings: IncludePayloads / MaxBodyBytes.
    ///  - Robust content buffering/restoration so downstream can still read the body.
    /// </summary>
    public sealed class SageApiLoggingHandler : DelegatingHandler
    {
        private const string CorrelationHeader = "X-Correlation-Id";

        private readonly ILogger<SageApiLoggingHandler> _logger;
        private readonly IDbLogWriter _dbLogWriter;
        private readonly SageApiLoggingOptions _logOpts;
        private readonly SageApiSettings _api;

        /// <summary>
        /// Options bound from configuration: SageApi:Logging
        /// </summary>
        public sealed class SageApiLoggingOptions
        {
            /// <summary>Enable/disable the logging handler (still executes call when disabled).</summary>
            public bool Enabled { get; set; } = true;

            /// <summary>If true, request/response bodies are captured (truncated to MaxBodyBytes).</summary>
            public bool IncludePayloads { get; set; } = true;

            /// <summary>Max characters captured for request/response bodies (safeguard against huge payloads).</summary>
            public int MaxBodyBytes { get; set; } = 65536;

            /// <summary>If true, the DB writer will encrypt bodies (handled inside DbLogWriter). Left here for completeness.</summary>
            public bool EncryptPayloads { get; set; } = true;
        }

        public SageApiLoggingHandler(
            ILogger<SageApiLoggingHandler> logger,
            IDbLogWriter dbLogWriter,
            IOptions<SageApiSettings> apiSettings,
            IOptionsMonitor<SageApiLoggingOptions> logOptions // bound from "SageApi:Logging"
        )
        {
            _logger = logger;
            _dbLogWriter = dbLogWriter;
            _api = apiSettings.Value;
            _logOpts = logOptions.CurrentValue;
        }

        /// <summary>
        /// Executes the outbound request, capturing a safe diagnostic frame of headers and (optionally) payloads.
        /// On 401, we emit an explicit warning with the header snapshot to aid root-cause analysis.
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            // Basic context
            var endpoint = request.RequestUri?.PathAndQuery ?? request.RequestUri?.ToString() ?? "";
            var method = request.Method.Method;

            // Correlation
            string correlationId = TryGetHeader(request, CorrelationHeader) ?? Guid.NewGuid().ToString("N");

            // -------- Capture request body (optional) & restore content afterwards --------
            string? reqBody = null;
            MediaTypeHeaderValue? reqContentType = request.Content?.Headers.ContentType;

            if (_logOpts.Enabled && _logOpts.IncludePayloads && request.Content != null)
            {
                try
                {
                    var raw = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    reqBody = Truncate(raw, _logOpts.MaxBodyBytes);
                    // Rebuild content so the pipeline can still send it
                    var media = reqContentType?.MediaType ?? "application/json";
                    request.Content = new StringContent(raw, Encoding.UTF8, media);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to capture request body for {Method} {Endpoint}", method, endpoint);
                }
            }

            // -------- Snapshot headers (presence + safe redaction) --------
            var hdr = BuildHeaderSnapshot(request, _api);

            HttpResponseMessage? resp = null;
            string? respBody = null;
            int statusCode = 0;
            MediaTypeHeaderValue? respContentType = null;

            try
            {
                resp = await base.SendAsync(request, ct).ConfigureAwait(false);
                statusCode = (int)resp.StatusCode;

                if (_logOpts.Enabled && _logOpts.IncludePayloads && resp.Content != null)
                {
                    respContentType = resp.Content.Headers.ContentType;
                    var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    respBody = Truncate(raw, _logOpts.MaxBodyBytes);

                    // Put back the response content so callers down the pipeline can read again
                    var media = respContentType?.MediaType ?? "application/json";
                    resp.Content = new StringContent(raw, Encoding.UTF8, media);
                }

                // If Unauthorized, emit a concise probe frame (headers, auth challenge, small body preview)
                if ((int)resp.StatusCode == 401)
                {
                    var authChallenge = resp.Headers.WwwAuthenticate is { } authVals && authVals.Any()
                        ? string.Join(",", authVals.Select(a => a.Scheme))
                        : null;

                    _logger.LogWarning(
                        "Sage 401 Unauthorized. {Method} {Endpoint} site={Site} company={Company} apiKey={ApiKey} auth={Auth} corr={CorrelationId} bodyPreview={BodyPreview}",
                        method, endpoint,
                        hdr.Site ?? "(null)",
                        hdr.Company ?? "(null)",
                        hdr.ApiKeyPresent ? "(present)" : "(missing)",
                        string.IsNullOrWhiteSpace(hdr.AuthorizationScheme) ? "(none)" : hdr.AuthorizationScheme,
                        correlationId,
                        Preview(respBody, 256)
                    );
                }

                return resp;
            }
            catch (Exception ex)
            {
                // For exceptions, keep diagnostic information and rethrow.
                statusCode = 0;
                respBody = JsonSerializer.Serialize(new { error = ex.Message });
                _logger.LogWarning(ex, "Sage call failed {Method} {Endpoint}", method, endpoint);
                throw;
            }
            finally
            {
                sw.Stop();

                if (_logOpts.Enabled)
                {
                    // ---------- ApiLogs (full payloads; encryption handled by DbLogWriter) ----------
                    var api = new ApiLogRecord
                    {
                        Endpoint = endpoint,
                        RequestMethod = method,
                        RequestPayloadEncrypted = _logOpts.IncludePayloads ? reqBody : null,
                        ResponsePayloadEncrypted = _logOpts.IncludePayloads ? respBody : null,
                        HttpStatusCode = statusCode,
                        CallerId = null,
                        ApiType = "Sage200.API" // kept generic; adjust per feature if you want
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
                    var resource = InferResource(endpoint);
                    var action = InferAction(method, endpoint);

                    var details = new
                    {
                        correlationId,
                        status = statusCode,
                        durationMs = sw.ElapsedMilliseconds,
                        endpoint,
                        method,
                        requestContentType = reqContentType?.MediaType,
                        responseContentType = respContentType?.MediaType,
                        // Header snapshot (safe for audit)
                        headers = new
                        {
                            site = hdr.Site,
                            company = hdr.Company,
                            apiKeyPresent = hdr.ApiKeyPresent,
                            authorizationScheme = hdr.AuthorizationScheme
                        }
                    };
                    var detailsJson = JsonSerializer.Serialize(details);

                    var audit = new AuditLogRecord
                    {
                        EventType = 0,   // API
                        Category = 1,    // Sage200
                        Severity = severityInt,
                        Status = statusInt,
                        IpAddress = "0.0.0.0",
                        Resource = resource,
                        Action = action,
                        Description = $"Outbound {method} {endpoint}",
                        Details = detailsJson,
                        CorrelationId = correlationId,
                        HttpMethod = method,
                        UrlPath = endpoint,
                        UserAgent = "Sage200Microservice/HttpClient",
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
        }

        #region Helpers

        /// <summary>Safely truncates a string to the requested maximum length.</summary>
        private static string Truncate(string? s, int max)
            => string.IsNullOrEmpty(s) ? s ?? "" : (s.Length <= max ? s : s.Substring(0, max));

        /// <summary>Very short preview for inline logs.</summary>
        private static string Preview(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = Truncate(s, max);
            return t.Replace("\r", " ").Replace("\n", " ");
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
            if (endpoint.IndexOf("/sop_orders", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Sage200.SOP.Order";
            if (endpoint.IndexOf("/customers", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Sage200.Customers";
            return "Sage200.API";
        }

        private static string InferAction(string method, string endpoint)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
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

        /// <summary>
        /// Builds a minimal, privacy-safe header snapshot to help diagnose 401s (and other auth issues).
        /// </summary>
        private static HeaderSnapshot BuildHeaderSnapshot(HttpRequestMessage request, SageApiSettings api)
        {
            string? site = null, company = null;
            bool apiKeyPresent = false;
            string? authScheme = null;

            // X-Site / X-Company (names configurable)
            if (!string.IsNullOrWhiteSpace(api.SiteHeaderName) &&
                request.Headers.TryGetValues(api.SiteHeaderName, out var siteVals))
            {
                site = siteVals.FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(api.CompanyHeaderName) &&
                request.Headers.TryGetValues(api.CompanyHeaderName, out var compVals))
            {
                company = compVals.FirstOrDefault();
            }

            // X-Api-Key presence (do NOT log its value)
            if (!string.IsNullOrWhiteSpace(api.ApiKeyHeaderName) &&
                request.Headers.TryGetValues(api.ApiKeyHeaderName, out var keyVals))
            {
                apiKeyPresent = keyVals.Any() && !string.IsNullOrWhiteSpace(keyVals.FirstOrDefault());
            }

            // Authorization scheme (e.g., "Bearer")
            if (request.Headers.Authorization is AuthenticationHeaderValue auth)
            {
                authScheme = auth.Scheme;
            }
            else if (request.Headers.TryGetValues("Authorization", out var anyAuth) && anyAuth.Any())
            {
                // Safety: only report the scheme part if it looks like "Bearer xxx"
                var first = anyAuth.First();
                var space = first?.IndexOf(' ') ?? -1;
                authScheme = space > 0 ? first!.Substring(0, space) : "(present)";
            }

            return new HeaderSnapshot(site, company, apiKeyPresent, authScheme);
        }

        private readonly record struct HeaderSnapshot(string? Site, string? Company, bool ApiKeyPresent, string? AuthorizationScheme);

        #endregion
    }
}
