using System;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Minimal token status for “/auth/status” and health views.
    /// </summary>
    public sealed class TokenInfo
    {
        public DateTimeOffset AccessTokenExpiresUtc { get; init; }
        public bool HasRefreshToken { get; init; }
    }
}
