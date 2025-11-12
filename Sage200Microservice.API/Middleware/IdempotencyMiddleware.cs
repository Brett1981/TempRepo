using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Global HTTP idempotency for POST requests.
    /// - Uses header Idempotency-Key or generates canonical SHA-256 HEX from JSON body when missing.
    /// - Looks up stored full HTTP response and short-circuits duplicates (status/headers/body).
    /// - Persists first successful response (capped at 512 KiB body).
    /// - Can be opted out via [SkipIdempotency] on controllers/actions.
    /// </summary>
    public sealed class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IdempotencyMiddleware> _log;
        private readonly IdempotencyMetrics _metrics;

        // 512 KiB cap (adjust if needed)
        private const int BodyPersistMaxBytes = 512 * 1024;

        private static readonly JsonSerializerOptions CanonicalJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> log, IdempotencyMetrics metrics)
        {
            _next = next; _log = log; _metrics = metrics;
        }

        public async Task InvokeAsync(HttpContext ctx, IIdempotencyRecordRepository repo)
        {
            // Only enforce on POSTs that haven't opted out
            if (!HttpMethods.IsPost(ctx.Request.Method) || IsOptedOut(ctx))
            {
                await _next(ctx);
                return;
            }

            // 1) Resolve/generate key
            string? idemKey = TryGetHeader(ctx.Request, "Idempotency-Key");
            string? requestHash = null;
            if (string.IsNullOrWhiteSpace(idemKey))
            {
                (idemKey, requestHash) = await ComputeDeterministicKeyFromBodyAsync(ctx.Request);
                ctx.Request.Headers["Idempotency-Key"] = idemKey!;
            }

            // 2) Lookup & replay (repo hashes: Base64(SHA-512) of key)
            var existing = await repo.GetByKeyHashAsync(HashKeySha512Base64(idemKey!), ctx.RequestAborted);
            _metrics.IdempotentLookupsInc();

            if (existing?.ResponseStatusCode is int status)
            {
                _log.LogInformation("Idempotent replay: {Path}", ctx.Request.Path);
                _metrics.IdempotentHitsInc();

                if (!string.IsNullOrWhiteSpace(existing.ResponseContentType))
                    ctx.Response.ContentType = existing.ResponseContentType;

                WriteStoredHeaders(existing.ResponseHeaders, ctx.Response.Headers);

                ctx.Response.StatusCode = status;
                if (!string.IsNullOrEmpty(existing.ResponseBody))
                    await ctx.Response.WriteAsync(existing.ResponseBody);

                return;
            }

            // 3) First execution → capture and persist
            var originalBody = ctx.Response.Body;
            await using var capture = new MemoryStream();
            ctx.Response.Body = capture;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await _next(ctx);
            }
            finally
            {
                sw.Stop();
                _metrics.ReplayLatencyObserve(sw.ElapsedMilliseconds);

                capture.Position = 0;
                var bodyBytes = capture.ToArray();
                string? bodyText = null;
                if (bodyBytes.Length <= BodyPersistMaxBytes)
                {
                    bodyText = Encoding.UTF8.GetString(bodyBytes);
                }
                else
                {
                    _log.LogWarning("Response body exceeded {Cap} bytes; metadata only persisted for {Path}",
                        BodyPersistMaxBytes, ctx.Request.Path);
                }

                var headersJson = SerializeHeaders(ctx.Response.Headers);

                await repo.UpsertHttpResponseAsync(
                    idempotencyKey: idemKey!,
                    resource: ResolveResource(ctx.Request.Path),
                    statusCode: ctx.Response.StatusCode,
                    contentType: ctx.Response.ContentType,
                    headersJson: headersJson,
                    body: bodyText,
                    requestHash: requestHash,
                    expiresUtc: null,
                    ct: ctx.RequestAborted);

                // copy back to client stream
                capture.Position = 0;
                await capture.CopyToAsync(originalBody, ctx.RequestAborted);
                ctx.Response.Body = originalBody;
            }
        }

        private static string? TryGetHeader(HttpRequest req, string name)
            => req.Headers.TryGetValue(name, out StringValues v) && !StringValues.IsNullOrEmpty(v) ? v.ToString() : null;

        private static bool IsOptedOut(HttpContext ctx)
            => ctx.GetEndpoint()?.Metadata?.GetMetadata<SkipIdempotencyAttribute>() is not null;

        private static string? ResolveResource(PathString path)
            => path.Value?.Trim('/').ToLowerInvariant();

        private static async Task<(string key, string requestHash)> ComputeDeterministicKeyFromBodyAsync(HttpRequest req)
        {
            req.EnableBuffering();
            using var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            req.Body.Position = 0;

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var canonical = JsonSerializer.Serialize(doc.RootElement, CanonicalJson);

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var hex = Convert.ToHexString(bytes); // UPPERCASE

            return (hex, hex); // requestHash mirrors the canonical key for diagnostics
        }

        private static string HashKeySha512Base64(string key)
        {
            using var sha = System.Security.Cryptography.SHA512.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty));
            return Convert.ToBase64String(hash);
        }

        private static bool IsSensitiveHeader(string name)
            => name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Authorization", StringComparison.OrdinalIgnoreCase);

        private static string SerializeHeaders(IHeaderDictionary headers)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
            {
                if (IsSensitiveHeader(h.Key)) continue;
                map[h.Key] = h.Value.ToArray();
            }
            return JsonSerializer.Serialize(map);
        }

        private static void WriteStoredHeaders(string? storedJson, IHeaderDictionary target)
        {
            if (string.IsNullOrWhiteSpace(storedJson)) return;
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string[]>>(storedJson);
                if (map is null) return;
                foreach (var kv in map)
                {
                    if (IsSensitiveHeader(kv.Key)) continue;
                    target[kv.Key] = kv.Value;
                }
            }
            catch { /* ignore bad header payloads */ }
        }
    }

    /// <summary>Opt-out marker for endpoints that must skip idempotency.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class SkipIdempotencyAttribute : Attribute { }

    /// <summary>Minimal OTEL metrics wrapper for idempotency signals.</summary>
    public sealed class IdempotencyMetrics
    {
        private readonly System.Diagnostics.Metrics.Meter _meter;
        private readonly System.Diagnostics.Metrics.Counter<long> _lookups;
        private readonly System.Diagnostics.Metrics.Counter<long> _hits;
        private readonly System.Diagnostics.Metrics.Histogram<long> _replayLatencyMs;

        public IdempotencyMetrics()
        {
            _meter = new("Sage200Microservice.Idempotency");
            _lookups = _meter.CreateCounter<long>("http_idempotent_lookups_total");
            _hits = _meter.CreateCounter<long>("http_idempotent_hits_total");
            _replayLatencyMs = _meter.CreateHistogram<long>("http_idempotent_replay_latency_ms");
        }

        public void IdempotentLookupsInc() => _lookups.Add(1);
        public void IdempotentHitsInc() => _hits.Add(1);
        public void ReplayLatencyObserve(long ms) => _replayLatencyMs.Record(ms);
    }
}