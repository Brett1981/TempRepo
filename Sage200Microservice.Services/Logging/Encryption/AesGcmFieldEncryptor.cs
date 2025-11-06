using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Logging.Encryption;
using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sage200Microservice.Services.Security
{
    /// <summary>
    /// AES-256-GCM field encryptor.
    /// Token format (primary): "A1:" + base64url( nonce(12) | cipher | tag(16) )
    /// Back-compat: accepts tokens without prefix, with base64 (not url-safe),
    /// and the alternate byte layout nonce | tag | cipher.
    /// No AAD is enforced to keep compatibility with existing tokens.
    /// </summary>
    public sealed class AesGcmFieldEncryptor : IFieldEncryptor
    {
        public sealed class Options
        {
            /// <summary>Preferred property name (keep your current appsettings): Logging:ApiLogs:EncryptionKey</summary>
            public string? EncryptionKey { get; set; }

            /// <summary>Back-compat alias accepted if EncryptionKey is not present.</summary>
            public string? Base64Key { get; set; }
        }

        private const string Prefix = "A1:"; // version marker
        private readonly byte[] _key;

        public AesGcmFieldEncryptor(IOptions<Options> opt)
        {
            var b64 = !string.IsNullOrWhiteSpace(opt.Value.EncryptionKey)
                      ? opt.Value.EncryptionKey
                      : opt.Value.Base64Key;

            if (string.IsNullOrWhiteSpace(b64))
                throw new InvalidOperationException("Missing encryption key (Logging:ApiLogs:EncryptionKey).");

            try { _key = Convert.FromBase64String(NormalizeB64(b64)); }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Logging:ApiLogs:EncryptionKey must be valid Base64.", ex);
            }

            if (_key.Length != 32)
                throw new InvalidOperationException($"Logging:ApiLogs:EncryptionKey must decode to 32 bytes. Got: {_key.Length}.");
        }

        // ----------------------
        // Encrypt (unchanged API)
        // ----------------------
        public string EncryptToToken(string plaintext)
        {
            if (plaintext is null) plaintext = string.Empty;

            var nonce = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                RandomNumberGenerator.Fill(nonce.AsSpan(0, 12));

                // plaintext → bytes (UTF8)
                var plainBytes = Encoding.UTF8.GetBytes(plaintext);
                var cipherBytes = new byte[plainBytes.Length];
                var tag = new byte[16];

                using var aes = new AesGcm(_key);
                aes.Encrypt(nonce.AsSpan(0, 12), plainBytes, cipherBytes, tag, associatedData: null);

                // Assemble nonce|cipher|tag and encode base64url with version prefix
                var totalLen = 12 + cipherBytes.Length + 16;
                var buffer = ArrayPool<byte>.Shared.Rent(totalLen);
                try
                {
                    var span = buffer.AsSpan(0, totalLen);
                    nonce.AsSpan(0, 12).CopyTo(span);
                    cipherBytes.AsSpan().CopyTo(span.Slice(12));
                    tag.AsSpan().CopyTo(span.Slice(12 + cipherBytes.Length));

                    return Prefix + ToBase64Url(span);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(nonce);
            }
        }

        // ----------------------
        // Decrypt (new)
        // ----------------------
        public string DecryptFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            // Strip version prefix if present
            if (token.StartsWith(Prefix, StringComparison.Ordinal))
                token = token.Substring(Prefix.Length);

            // Base64url → Base64 normalize and decode
            var raw = DecodeFlexibleBase64(token);

            if (raw.Length < 12 + 16)
                throw new CryptographicException("Encrypted token is too short.");

            // Try layout #1: nonce | cipher | tag
            if (TryDecrypt(raw, nonceAt: 0, tagAtEnd: true, out var text))
                return text;

            // Try layout #2: nonce | tag | cipher (alternate)
            if (TryDecrypt(raw, nonceAt: 0, tagAtEnd: false, out text))
                return text;

            throw new CryptographicException("Failed to decrypt token (invalid format or key).");
        }

        // Attempt decryption with two possible layouts.
        private bool TryDecrypt(byte[] raw, int nonceAt, bool tagAtEnd, out string plaintext)
        {
            plaintext = string.Empty;

            var nonce = raw.AsSpan(nonceAt, 12);

            ReadOnlySpan<byte> cipherSpan, tagSpan;
            if (tagAtEnd)
            {
                // nonce | cipher | tag
                var cipherLen = raw.Length - 12 - 16;
                if (cipherLen < 0) return false;

                cipherSpan = raw.AsSpan(12, cipherLen);
                tagSpan = raw.AsSpan(12 + cipherLen, 16);
            }
            else
            {
                // nonce | tag | cipher
                var cipherLen = raw.Length - 12 - 16;
                if (cipherLen < 0) return false;

                tagSpan = raw.AsSpan(12, 16);
                cipherSpan = raw.AsSpan(12 + 16, cipherLen);
            }

            // Decrypt
            byte[] plain = new byte[cipherSpan.Length];
            try
            {
                using var aes = new AesGcm(_key);
                aes.Decrypt(nonce, cipherSpan, tagSpan, plain, associatedData: null);
                plaintext = Encoding.UTF8.GetString(plain);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        // Helpers
        private static string ToBase64Url(ReadOnlySpan<byte> bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string NormalizeB64(string s)
        {
            // accept base64url/standard and add padding if required
            s = s.Replace('-', '+').Replace('_', '/');
            var pad = 4 - (s.Length % 4);
            if (pad is > 0 and < 4) s = s.PadRight(s.Length + pad, '=');
            return s;
        }

        private static byte[] DecodeFlexibleBase64(string token)
        {
            try { return Convert.FromBase64String(NormalizeB64(token)); }
            catch (FormatException)
            {
                // last resort: maybe it’s already standard base64
                return Convert.FromBase64String(token);
            }
        }

        /// <summary>
        /// Heuristic: does <paramref name="s"/> look like an access/refresh token?
        /// Recognizes JWT/JWE (3 or 5 base64url parts), PASETO (vX.local/public),
        /// Azure SAS fragments, or long opaque base64url strings.
        /// </summary>
        public bool MightBeToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            // Strip "Bearer " prefix if it slipped through
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("Bearer ".Length).Trim();

            if (s.Length < 16) return false; // too short to be a real token

            // 1) JWT / JWE: dot-separated base64url segments (3 for JWT, 5 for JWE)
            var parts = s.Split('.');
            if ((parts.Length == 3 || parts.Length == 5) &&
                Array.TrueForAll(parts, p => p.Length > 0 && Regex.IsMatch(p, "^[A-Za-z0-9_-]+$")))
            {
                // Try to decode header to further confirm (won't throw if it's not really base64url)
                try
                {
                    var headerJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                    if (headerJson.Contains("\"alg\"") || headerJson.Contains("\"typ\"") || headerJson.Contains("\"kid\""))
                        return true;
                }
                catch
                {
                    // Even if header isn't decodable, the shape still strongly suggests a token.
                    return true;
                }
                return true;
            }

            // 2) PASETO: vX.local.|vX.public. + base64url
            if (Regex.IsMatch(s, @"^v\d\.(local|public)\.[A-Za-z0-9\-_]+$"))
                return true;

            // 3) Azure SAS style (when only the fragment is passed)
            // e.g. "sr=...&sig=...&se=...&skn=..."
            if (s.StartsWith("SharedAccessSignature ", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("&sig=", StringComparison.Ordinal) ||
                s.StartsWith("sr=", StringComparison.Ordinal) ||
                s.StartsWith("sig=", StringComparison.Ordinal))
                return true;

            // 4) Long opaque base64url-ish strings (common for provider tokens)
            if (s.Length >= 32 && Regex.IsMatch(s, @"^[A-Za-z0-9\-_]+={0,2}$"))
                return true;

            return false;

            static byte[] Base64UrlDecode(string input)
            {
                // base64url -> base64
                string padded = input.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                return Convert.FromBase64String(padded);
            }
        }
    }
}
