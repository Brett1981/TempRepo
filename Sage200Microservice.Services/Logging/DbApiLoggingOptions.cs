using System.Collections.Generic;

namespace Sage200Microservice.Services.Logging
{
    /// <summary>
    /// Options that control outbound Sage API request/response logging
    /// into dbo.ApiLogs (and coordinate with AuditLogs).
    /// Bound from configuration section: "SageApi:Logging".
    /// </summary>
    public sealed class DbApiLoggingOptions
    {
        /// <summary>Master switch for the logging handler.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Log serialized request/response bodies (subject to MaxBodyBytes).
        /// If false, only method/URL/status/headers are logged.
        /// </summary>
        public bool IncludePayloads { get; set; } = true;

        /// <summary>
        /// When IncludePayloads is true, payloads/selected headers will be encrypted
        /// using the configured IFieldEncryptor (AesGcm) before persisting to ApiLogs.
        /// </summary>
        public bool EncryptPayloads { get; set; } = true;

        /// <summary>Base64-encoded 32-byte AES key.</summary>
        public string? EncryptionKey { get; set; }
        /// <summary>
        /// Max number of bytes to capture per request/response body (after which
        /// the payload is truncated and marked as such in the log record).
        /// </summary>
        public int MaxBodyBytes { get; set; } = 64 * 1024; // 64 KiB default

        /// <summary>
        /// Logical API type tag written to ApiLogs.ApiType (e.g., "Sage200.SOP").
        /// Override per client if you host multiple API types.
        /// </summary>
        public string ApiType { get; set; } = "Sage200";

        /// <summary>
        /// Include non-sensitive headers in logs. Sensitive headers are always redacted/encrypted.
        /// </summary>
        public bool CaptureHeaders { get; set; } = true;

        /// <summary>
        /// Headers that are considered sensitive and will be redacted or encrypted
        /// (e.g., Authorization, X-Api-Key, Set-Cookie).
        /// </summary>
        public HashSet<string> SensitiveHeaderNames { get; set; } = new HashSet<string>
        {
            "Authorization",
            "X-Api-Key",
            "Set-Cookie"
        };

        /// <summary>
        /// If true, the handler records timing/duration and basic sizes for request/response.
        /// </summary>
        public bool CaptureTimings { get; set; } = true;

        /// <summary>
        /// If true, binary or non-UTF8 payloads are skipped (length is still recorded).
        /// </summary>
        public bool SkipNonTextPayloads { get; set; } = true;
    }
}
