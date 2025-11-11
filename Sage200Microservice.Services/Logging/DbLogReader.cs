using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data;
using Sage200Microservice.Services.Logging.Encryption;
using Sage200Microservice.Services.Security;
using System.Data.Common;
using System.Text.Json;

namespace Sage200Microservice.Services.Logging;

public sealed class DbLogReader : IDbLogReader
{
    private readonly ApplicationContext _db;
    private readonly IFieldEncryptor _enc;

    public DbLogReader(ApplicationContext db, IFieldEncryptor enc)
    {
        _db = db;
        _enc = enc;
    }

    public async Task<IReadOnlyList<ApiLogDto>> GetApiLogsAsync(int skip, int take, CancellationToken ct)
    {
        var list = new List<ApiLogDto>();
        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Endpoint, RequestMethod, RequestPayload, ResponsePayload, HttpStatusCode, Timestamp, CallerId, ApiType
FROM dbo.ApiLogs
ORDER BY Id DESC
OFFSET @skip ROWS
FETCH NEXT @take ROWS ONLY;";
        AddParam(cmd, "@skip", skip);
        AddParam(cmd, "@take", take);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var req = GetString(rdr, "RequestPayload") ?? string.Empty;
            var res = GetString(rdr, "ResponsePayload") ?? string.Empty;

            var dto = new ApiLogDto
            {
                Id = GetInt32FromAny(rdr, "Id") ?? 0,
                Endpoint = GetString(rdr, "Endpoint") ?? string.Empty,
                RequestMethod = GetString(rdr, "RequestMethod") ?? string.Empty,
                HttpStatusCode = GetInt32FromAny(rdr, "HttpStatusCode") ?? 0,
                Timestamp = GetDateTime(rdr, "Timestamp") ?? DateTime.UtcNow,
                CallerId = GetString(rdr, "CallerId") ?? string.Empty,
                ApiType = GetString(rdr, "ApiType") ?? string.Empty
            };

            // Server-side decrypt if in token format; else pass-through.
            if (_enc.MightBeToken(req))
            {
                dto.RequestEncrypted = true;
                dto.RequestPayload = SafeDecrypt(req);
            }
            else dto.RequestPayload = req;

            if (_enc.MightBeToken(res))
            {
                dto.ResponseEncrypted = true;
                dto.ResponsePayload = SafeDecrypt(res);
            }
            else dto.ResponsePayload = res;

            list.Add(dto);
        }

        return list;

        string SafeDecrypt(string token)
        {
            try { return _enc.DecryptFromToken(token); }
            catch { return "[[decrypt-failed]]"; }
        }
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(int skip, int take, CancellationToken ct)
    {
        var list = new List<AuditLogDto>();
        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Timestamp, EventType, Category, Severity, UserId, ClientId, IpAddress,
       Resource, Action, Status, Description, Details, CorrelationId,
       HttpMethod, UrlPath, HttpStatusCode, DurationMs, UserAgent, ReferenceId, ReferenceName
FROM dbo.AuditLogs
ORDER BY Id DESC
OFFSET @skip ROWS
FETCH NEXT @take ROWS ONLY;";
        AddParam(cmd, "@skip", skip);
        AddParam(cmd, "@take", take);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var dto = new AuditLogDto
            {
                Id = GetInt64FromAny(rdr, "Id") ?? 0L,
                Timestamp = GetDateTime(rdr, "Timestamp") ?? DateTime.UtcNow,

                // These may be INT in some envs, NVARCHAR in others — read defensively:
                EventType = GetInt32FromAny(rdr, "EventType") ?? 0,
                Category = GetInt32FromAny(rdr, "Category") ?? 0,
                Severity = GetInt32FromAny(rdr, "Severity") ?? 0,

                UserId = GetString(rdr, "UserId"),
                ClientId = GetString(rdr, "ClientId"),
                IpAddress = GetString(rdr, "IpAddress") ?? string.Empty,
                Resource = GetString(rdr, "Resource") ?? string.Empty,
                Action = GetString(rdr, "Action") ?? string.Empty,

                Status = GetInt32FromAny(rdr, "Status") ?? 0,
                Description = GetString(rdr, "Description") ?? string.Empty,
                Details = GetString(rdr, "Details") ?? string.Empty,
                CorrelationId = GetString(rdr, "CorrelationId") ?? string.Empty,

                HttpMethod = GetString(rdr, "HttpMethod") ?? string.Empty,
                UrlPath = GetString(rdr, "UrlPath") ?? string.Empty,

                HttpStatusCode = GetInt32FromAny(rdr, "HttpStatusCode"),
                DurationMs = GetInt64FromAny(rdr, "DurationMs"),
                UserAgent = GetString(rdr, "UserAgent") ?? string.Empty,
                ReferenceId = GetString(rdr, "ReferenceId"),
                ReferenceName = GetString(rdr, "ReferenceName")
            };

            list.Add(dto);
        }
        return list;
    }

    // ---------- helpers ----------

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static string? GetString(DbDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return null;
        // If actual field type is not string, ToString() still yields a sensible value
        return r.GetValue(ord)?.ToString();
    }

    private static DateTime? GetDateTime(DbDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return null;

        var v = r.GetValue(ord);
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v?.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetInt32FromAny(DbDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return null;

        var v = r.GetValue(ord);
        if (v is int i) return i;
        if (v is long l && l <= int.MaxValue && l >= int.MinValue) return (int)l;
        if (int.TryParse(v?.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static long? GetInt64FromAny(DbDataReader r, string name)
    {
        var ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return null;

        var v = r.GetValue(ord);
        if (v is long l) return l;
        if (v is int i) return (long)i;
        if (long.TryParse(v?.ToString(), out var parsed)) return parsed;
        return null;
    }
}
