using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data;
using Sage200Microservice.Services.Logging;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Admin/diagnostics endpoints used by business-dashboard.html
    /// Security:
    ///  - In Development the 'ApiUser' policy is permissive (Program.cs).
    ///  - In non-Dev, your API-key middleware must be used; policy requires an API key header or an authenticated user.
    /// </summary>
    [ApiController]
    [Route("api/admin")]
    [Authorize(Policy = "ApiUser")] // policy is registered in Program.cs
    public class AdminController : ControllerBase
    {
        private readonly ApplicationContext _db;
        private readonly IDbLogReader _logReader;

        public AdminController(ApplicationContext db, IDbLogReader logReader)
        {
            _db = db;
            _logReader = logReader;
        }

        // ----------------------------------------------------------------
        // TOKENS
        // ----------------------------------------------------------------

        /// <summary>
        /// GET /api/admin/tokens
        /// Returns a masked snapshot of OAuth tokens (no secrets).
        /// </summary>
        [HttpGet("tokens")]
        public async Task<IActionResult> GetTokens(CancellationToken ct)
        {
            // Schema: OAuthTokens(Id, Provider, Audience, ProtectedRefreshToken, AccessTokenExpiresUtc, UpdatedUtc, Scope)
            // IMPORTANT: CAST to bit so EF can materialize to C# bool.
            var rows = await _db.Database
                .SqlQueryRaw<TokenRow>(@"
SELECT TOP 50 
    Id, 
    Provider, 
    Audience, 
    CAST(CASE 
            WHEN ProtectedRefreshToken IS NOT NULL 
                 AND LTRIM(RTRIM(ProtectedRefreshToken)) <> '' 
            THEN 1 ELSE 0 
        END AS bit) AS HasRefreshToken,
    AccessTokenExpiresUtc,
    UpdatedUtc,
    Scope
FROM dbo.OAuthTokens
ORDER BY UpdatedUtc DESC")
                .ToListAsync(ct);

            return Ok(rows);
        }

        public sealed class TokenRow
        {
            public int Id { get; set; }
            public string Provider { get; set; } = "";
            public string Audience { get; set; } = "";
            public bool HasRefreshToken { get; set; }
            public DateTimeOffset? AccessTokenExpiresUtc { get; set; }
            public DateTimeOffset UpdatedUtc { get; set; }
            public string? Scope { get; set; }
        }

        // ----------------------------------------------------------------
        // API LOGS (Sage I/O) — supports both /api-logs and legacy /apilogs
        // ----------------------------------------------------------------

        /// <summary>
        /// GET /api/admin/api-logs  (preferred)
        /// GET /api/admin/apilogs   (legacy)
        /// Returns recent API logs; server performs decryption when needed.
        /// </summary>
        [HttpGet("api-logs")]
        [HttpGet("apilogs")] // legacy route used by early dashboard builds
        public async Task<IActionResult> GetApiLogs([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 500);
            skip = Math.Max(0, skip);

            var result = await _logReader.GetApiLogsAsync(skip, take, ct);
            return Ok(result);
        }

        // ----------------------------------------------------------------
        // AUDIT LOGS — supports both /audit-logs and legacy /auditlogs
        // ----------------------------------------------------------------

        /// <summary>
        /// GET /api/admin/audit-logs  (preferred)
        /// GET /api/admin/auditlogs   (legacy)
        /// Returns recent audit logs (non-sensitive).
        /// </summary>
        [HttpGet("audit-logs")]
        [HttpGet("auditlogs")] // legacy route used by early dashboard builds
        public async Task<IActionResult> GetAuditLogs([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 500);
            skip = Math.Max(0, skip);

            var result = await _logReader.GetAuditLogsAsync(skip, take, ct);
            return Ok(result);
        }
    }
}
