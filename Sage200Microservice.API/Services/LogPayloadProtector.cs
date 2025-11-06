using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Sage200Microservice.API.Services
{
    /// <summary>
    /// Tries multiple strategies to decrypt stored payloads:
    /// 1) ASP.NET DataProtection Unprotect (purpose: "ApiLogPayload")
    /// 2) AES-GCM with a Base64 key from env var LOGS_AES_KEY (nonce+ciphertext+tag expected as Base64)
    /// Falls back to returning the original if decryption fails.
    /// </summary>
    public interface ILogPayloadProtector
    {
        bool TryDecrypt(string? input, out string? decrypted);
    }

    public sealed class LogPayloadProtector : ILogPayloadProtector
    {
        private readonly IDataProtector _protector;
        private readonly ILogger<LogPayloadProtector> _log;
        private readonly byte[]? _aesKey;

        public LogPayloadProtector(IDataProtectionProvider provider, ILogger<LogPayloadProtector> log)
        {
            _protector = provider.CreateProtector("ApiLogPayload");
            _log = log;

            var k = Environment.GetEnvironmentVariable("LOGS_AES_KEY");
            if (!string.IsNullOrWhiteSpace(k))
            {
                try { _aesKey = Convert.FromBase64String(k); }
                catch (Exception ex) { _log.LogWarning(ex, "Invalid LOGS_AES_KEY"); }
            }
        }

        public bool TryDecrypt(string? input, out string? decrypted)
        {
            decrypted = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Attempt DataProtection
            try
            {
                decrypted = _protector.Unprotect(input);
                return true;
            }
            catch { /* next */ }

            // Attempt AES-GCM if key present; expect base64( nonce | ciphertext | tag )
            if (_aesKey is { Length: 16 or 24 or 32 })
            {
                try
                {
                    var blob = Convert.FromBase64String(input);
                    if (blob.Length > 12 + 16) // nonce(12) + tag(16) at least
                    {
                        var nonce = blob.AsSpan(0, 12).ToArray();
                        var tag = blob.AsSpan(blob.Length - 16, 16).ToArray();
                        var cipher = blob.AsSpan(12, blob.Length - 12 - 16).ToArray();

                        using var aes = new AesGcm(_aesKey);
                        var plain = new byte[cipher.Length];
                        aes.Decrypt(nonce, cipher, tag, plain);
                        decrypted = Encoding.UTF8.GetString(plain);
                        return true;
                    }
                }
                catch { /* fallthrough */ }
            }

            return false;
        }
    }
}
