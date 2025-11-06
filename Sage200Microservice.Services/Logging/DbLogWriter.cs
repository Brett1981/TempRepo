// File: Services/Logging/DbLogWriter.cs

using System;
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sage200Microservice.Services.Logging
{
    public interface IDbLogWriter
    {
        Task WriteAuditAsync(AuditLogRecord rec, CancellationToken ct = default);
        Task WriteApiAsync(ApiLogRecord rec, CancellationToken ct = default);
        string EncryptForStorage(string? plaintext);
        string DecryptFromToken(string token);
        bool MightBeToken(string s);
    }

    public sealed class DbLogWriter : IDbLogWriter
    {
        private readonly string _connString;
        private readonly IConfiguration _config;
        private readonly ILogger<DbLogWriter> _logger;
        private readonly DbApiLoggingOptions _opts;

        public DbLogWriter(
            IConfiguration config,
            IOptions<DbApiLoggingOptions> opts,
            ILogger<DbLogWriter> logger)
        {
            _logger = logger;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _opts = opts?.Value ?? new DbApiLoggingOptions();
            _connString = config.GetConnectionString("DefaultConnection")
                          ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing.");
        }

        // -------------------- PUBLIC API --------------------

        public async Task WriteAuditAsync(AuditLogRecord rec, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO [dbo].[AuditLogs]
([Timestamp],[EventType],[Category],[Severity],[UserId],[ClientId],[IpAddress],[Resource],[Action],
 [Status],[Description],[Details],[CorrelationId],[HttpMethod],[UrlPath],[HttpStatusCode],[DurationMs],
 [UserAgent],[ReferenceId],[ReferenceName],[PreviousState],[NewState],[RetentionDays],[ExpiresAt])
VALUES
(@Timestamp,@EventType,@Category,@Severity,@UserId,@ClientId,@IpAddress,@Resource,@Action,
 @Status,@Description,@Details,@CorrelationId,@HttpMethod,@UrlPath,@HttpStatusCode,@DurationMs,
 @UserAgent,@ReferenceId,@ReferenceName,@PreviousState,@NewState,@RetentionDays,@ExpiresAt);
";

            using var con = new SqlConnection(_connString);
            await con.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, con);

            cmd.Parameters.Add(new SqlParameter("@Timestamp", SqlDbType.DateTime2) { Value = rec.TimestampUtc });
            cmd.Parameters.Add(new SqlParameter("@EventType", SqlDbType.Int) { Value = rec.EventType });
            cmd.Parameters.Add(new SqlParameter("@Category", SqlDbType.Int) { Value = rec.Category });
            cmd.Parameters.Add(new SqlParameter("@Severity", SqlDbType.Int) { Value = rec.Severity });

            cmd.Parameters.Add(new SqlParameter("@UserId", SqlDbType.NVarChar, 100) { Value = (object?)NullIfEmpty(rec.UserId) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ClientId", SqlDbType.NVarChar, 100) { Value = (object?)NullIfEmpty(rec.ClientId) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@IpAddress", SqlDbType.NVarChar, 45) { Value = rec.IpAddress ?? "" });
            cmd.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 100) { Value = rec.Resource ?? "" });
            cmd.Parameters.Add(new SqlParameter("@Action", SqlDbType.NVarChar, 100) { Value = rec.Action ?? "" });

            cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = rec.Status });
            cmd.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, -1) { Value = rec.Description ?? "" });
            cmd.Parameters.Add(new SqlParameter("@Details", SqlDbType.NVarChar, -1) { Value = rec.Details ?? "{}" });
            cmd.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.NVarChar, 64) { Value = rec.CorrelationId ?? "" });
            cmd.Parameters.Add(new SqlParameter("@HttpMethod", SqlDbType.NVarChar, 10) { Value = rec.HttpMethod ?? "GET" });
            cmd.Parameters.Add(new SqlParameter("@UrlPath", SqlDbType.NVarChar, 2048) { Value = rec.UrlPath ?? "/" });

            cmd.Parameters.Add(new SqlParameter("@HttpStatusCode", SqlDbType.Int) { Value = rec.HttpStatusCode.HasValue ? rec.HttpStatusCode.Value : DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@DurationMs", SqlDbType.BigInt) { Value = rec.DurationMs.HasValue ? rec.DurationMs.Value : DBNull.Value });

            cmd.Parameters.Add(new SqlParameter("@UserAgent", SqlDbType.NVarChar, 512) { Value = rec.UserAgent ?? "" });
            cmd.Parameters.Add(new SqlParameter("@ReferenceId", SqlDbType.NVarChar, -1) { Value = (object?)NullIfEmpty(rec.ReferenceId) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ReferenceName", SqlDbType.NVarChar, -1) { Value = (object?)NullIfEmpty(rec.ReferenceName) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@PreviousState", SqlDbType.NVarChar, -1) { Value = (object?)NullIfEmpty(rec.PreviousState) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@NewState", SqlDbType.NVarChar, -1) { Value = (object?)NullIfEmpty(rec.NewState) ?? DBNull.Value });

            cmd.Parameters.Add(new SqlParameter("@RetentionDays", SqlDbType.Int) { Value = rec.RetentionDays });
            cmd.Parameters.Add(new SqlParameter("@ExpiresAt", SqlDbType.DateTime2) { Value = rec.ExpiresAtUtc.HasValue ? rec.ExpiresAtUtc.Value : DBNull.Value });

            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed writing AuditLogs record.");
                // Swallow to avoid breaking main flow; alternatively rethrow based on policy.
            }
        }

        public async Task WriteApiAsync(ApiLogRecord rec, CancellationToken ct = default)
        {
            // Encrypt payloads/headers if enabled; otherwise store as plaintext
            var req = PrepareForStorage(rec.RequestPayloadEncrypted);
            var resp = PrepareForStorage(rec.ResponsePayloadEncrypted);

            const string sql = @"
INSERT INTO [dbo].[ApiLogs]
([Endpoint],[RequestMethod],[RequestPayload],[ResponsePayload],[HttpStatusCode],[Timestamp],[CallerId],[ApiType])
VALUES
(@Endpoint,@RequestMethod,@RequestPayload,@ResponsePayload,@HttpStatusCode, SYSUTCDATETIME(), @CallerId,@ApiType);
";

            using var con = new SqlConnection(_connString);
            await con.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, con);

            cmd.Parameters.Add(new SqlParameter("@Endpoint", SqlDbType.NVarChar, 200) { Value = Truncate(rec.Endpoint, 200) });
            cmd.Parameters.Add(new SqlParameter("@RequestMethod", SqlDbType.NVarChar, 10) { Value = Truncate(rec.RequestMethod ?? "GET", 10) });
            cmd.Parameters.Add(new SqlParameter("@RequestPayload", SqlDbType.NVarChar, -1) { Value = req ?? "" });
            cmd.Parameters.Add(new SqlParameter("@ResponsePayload", SqlDbType.NVarChar, -1) { Value = resp ?? "" });
            cmd.Parameters.Add(new SqlParameter("@HttpStatusCode", SqlDbType.Int) { Value = rec.HttpStatusCode });
            cmd.Parameters.Add(new SqlParameter("@CallerId", SqlDbType.NVarChar, 100) { Value = Truncate(rec.CallerId ?? "system", 100) });
            cmd.Parameters.Add(new SqlParameter("@ApiType", SqlDbType.NVarChar, 30) { Value = Truncate(rec.ApiType ?? "Unknown", 30) });

            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed writing ApiLogs record.");
            }
        }

        // -------------------- ENCRYPTION / HELPERS --------------------

        public string EncryptForStorage(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return "";

            if (!_opts.EncryptPayloads)
                return plaintext;

            // Avoid double-encryption (already tokenized)
            if (MightBeToken(plaintext))
                return plaintext;

            var key = GetKeyOrNull();
            if (key is null)
            {
                _logger.LogWarning("ApiLogs encryption is enabled but no valid key is configured. Storing as plaintext.");
                return plaintext;
            }

            return AesGcmTokenize(plaintext, key);
        }

        public string DecryptFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";

            // If it's not a token, return as-is (backward compatibility)
            if (!MightBeToken(token))
                return token;

            var key = GetKeyOrNull();
            if (key is null)
                throw new InvalidOperationException("No encryption key configured to decrypt tokenized payload.");

            return AesGcmDetokenize(token, key);
        }

        public bool MightBeToken(string s)
        {
            // Our token format: base64url(header).base64url(nonce+ciphertext+tag)
            // We also prefix with "enc:" to make detection trivial.
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.StartsWith("enc:", StringComparison.Ordinal)) return true;

            // Heuristic: two dot-separated URL-safe base64 segments and total length > 24
            var parts = s.Split('.');
            if (parts.Length != 2) return false;
            if (parts[0].Length < 8 || parts[1].Length < 16) return false;
            return IsBase64Url(parts[0]) && IsBase64Url(parts[1]);
        }

        // Wrapper used by WriteApiAsync to honor options
        private string PrepareForStorage(string? payload)
            => _opts.EncryptPayloads ? EncryptForStorage(payload) : (payload ?? "");

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max);

        private static bool IsBase64Url(string s)
        {
            foreach (var ch in s)
            {
                // URL-safe base64 alphabet + optional padding-less
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')) return false;
            }
            return true;
        }

        private byte[]? GetKeyOrNull()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_opts.EncryptionKey)) return null;
                var raw = Convert.FromBase64String(_opts.EncryptionKey);
                if (raw.Length != 32)
                {
                    _logger.LogError("ApiLogs EncryptionKey must be 32 bytes (Base64). Got {Len} bytes.", raw.Length);
                    return null;
                }
                return raw;
            }
            catch (FormatException)
            {
                _logger.LogError("ApiLogs EncryptionKey is not valid Base64.");
                return null;
            }
        }

        // Format: "enc:" + b64url(nonce[12] + ciphertext + tag[16]) with a fixed header segment.
        private static string AesGcmTokenize(string plaintext, byte[] key)
        {
            var pt = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[pt.Length];
            var tag = new byte[16];

            using (var gcm = new AesGcm(key))
            {
                gcm.Encrypt(nonce, pt, cipher, tag);
            }

            var blob = new byte[nonce.Length + cipher.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
            Buffer.BlockCopy(cipher, 0, blob, nonce.Length, cipher.Length);
            Buffer.BlockCopy(tag, 0, blob, nonce.Length + cipher.Length, tag.Length);

            return "enc:" + ToBase64Url(blob);
        }

        private static string AesGcmDetokenize(string token, byte[] key)
        {
            var body = token.StartsWith("enc:", StringComparison.Ordinal) ? token.Substring(4) : token;
            var blob = FromBase64Url(body);

            if (blob.Length < 12 + 16)
                throw new InvalidOperationException("Invalid token payload.");

            var nonce = blob.AsSpan(0, 12).ToArray();
            var tag = blob.AsSpan(blob.Length - 16, 16).ToArray();
            var cipher = blob.AsSpan(12, blob.Length - 12 - 16).ToArray();

            var pt = new byte[cipher.Length];
            using (var gcm = new AesGcm(key))
            {
                gcm.Decrypt(nonce, cipher, tag, pt);
            }
            return Encoding.UTF8.GetString(pt);
        }

        private static string ToBase64Url(ReadOnlySpan<byte> bytes)
        {
            var b64 = Convert.ToBase64String(bytes);
            return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static byte[] FromBase64Url(string s)
        {
            var b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            return Convert.FromBase64String(b64);
        }
    }


}
