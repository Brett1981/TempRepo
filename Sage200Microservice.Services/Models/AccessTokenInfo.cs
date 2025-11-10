using System;
using System.Collections.Generic;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Non-sensitive snapshot of the CURRENT access token (decoded from JWT).
    /// No token values are exposed here.
    /// </summary>
    public sealed class AccessTokenInfo
    {
        public string? Audience { get; init; }
        public IReadOnlyList<string>? Scopes { get; init; }
        public string? Issuer { get; init; }
        public string? TenantId { get; init; }
        public string? ClientAppId { get; init; }
        public DateTimeOffset? ExpiresUtc { get; init; }

        public double? SecondsToExpiry =>
            ExpiresUtc is null ? null : (ExceedsNow() ? (ExpiresUtc.Value - DateTimeOffset.UtcNow).TotalSeconds : 0);

        private bool ExceedsNow() => ExpiresUtc!.Value > DateTimeOffset.UtcNow;
    }
}
