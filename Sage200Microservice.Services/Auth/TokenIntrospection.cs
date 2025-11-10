using Sage200Microservice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Sage200Microservice.Services.Auth
{
    /// <summary>
    /// Utility to decode a JWT (header.payload.signature) into AccessTokenInfo without exposing secrets.
    /// </summary>
    public static class TokenIntrospection
    {
        public static AccessTokenInfo? TryDecode(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            try
            {
                var parts = accessToken.Split('.');
                if (parts.Length < 2) return null;

                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var payload = JsonDocument.Parse(payloadJson);

                string? aud = payload.RootElement.TryGetProperty("aud", out var audEl)
                    ? audEl.ValueKind == JsonValueKind.Array
                        ? string.Join(' ', audEl.EnumerateArray().Select(e => e.GetString()))
                        : audEl.GetString()
                    : null;

                string? scopeRaw = payload.RootElement.TryGetProperty("scope", out var scopeEl) ? scopeEl.GetString() : null;
                var scopes = string.IsNullOrWhiteSpace(scopeRaw) ? null : scopeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                string? iss = payload.RootElement.TryGetProperty("iss", out var issEl) ? issEl.GetString() : null;
                string? tid = payload.RootElement.TryGetProperty("tid", out var tidEl) ? tidEl.GetString() : null;
                string? app = payload.RootElement.TryGetProperty("azp", out var azpEl) ? azpEl.GetString()
                              : payload.RootElement.TryGetProperty("client_id", out var cidEl) ? cidEl.GetString()
                              : null;

                DateTimeOffset? exp = null;
                if (payload.RootElement.TryGetProperty("exp", out var expEl) && expEl.ValueKind == JsonValueKind.Number)
                {
                    exp = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
                }

                return new AccessTokenInfo
                {
                    Audience = aud,
                    Scopes = scopes,
                    Issuer = iss,
                    TenantId = tid,
                    ClientAppId = app,
                    ExpiresUtc = exp
                };
            }
            catch
            {
                return null;
            }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string output = input.Replace('-', '+').Replace('_', '/');
            switch (output.Length % 4)
            {
                case 0: break;
                case 2: output += "=="; break;
                case 3: output += "="; break;
                default: throw new FormatException("Illegal base64url string!");
            }
            return Convert.FromBase64String(output);
        }
    }
}
