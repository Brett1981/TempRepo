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
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@skip"; pSkip.Value = skip; cmd.Parameters.Add(pSkip);
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = take; cmd.Parameters.Add(pTake);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var req = rdr.GetString(rdr.GetOrdinal("RequestPayload"));
            var res = rdr.GetString(rdr.GetOrdinal("ResponsePayload"));

            var dto = new ApiLogDto
            {
                Id = rdr.GetInt32(rdr.GetOrdinal("Id")),
                Endpoint = rdr.GetString(rdr.GetOrdinal("Endpoint")),
                RequestMethod = rdr.GetString(rdr.GetOrdinal("RequestMethod")),
                HttpStatusCode = rdr.GetInt32(rdr.GetOrdinal("HttpStatusCode")),
                Timestamp = rdr.GetDateTime(rdr.GetOrdinal("Timestamp")),
                CallerId = rdr.GetString(rdr.GetOrdinal("CallerId")),
                ApiType = rdr.GetString(rdr.GetOrdinal("ApiType"))
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
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@skip"; pSkip.Value = skip; cmd.Parameters.Add(pSkip);
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = take; cmd.Parameters.Add(pTake);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var dto = new AuditLogDto
            {
                Id = rdr.GetInt64(0),
                Timestamp = rdr.GetDateTime(1),
                EventType = rdr.GetInt32(2),
                Category = rdr.GetInt32(3),
                Severity = rdr.GetInt32(4),
                UserId = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                ClientId = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                IpAddress = rdr.GetString(7),
                Resource = rdr.GetString(8),
                Action = rdr.GetString(9),
                Status = rdr.GetInt32(10),
                Description = rdr.GetString(11),
                Details = rdr.GetString(12),
                CorrelationId = rdr.GetString(13),
                HttpMethod = rdr.GetString(14),
                UrlPath = rdr.GetString(15),
                HttpStatusCode = rdr.IsDBNull(16) ? null : rdr.GetInt32(16),
                DurationMs = rdr.IsDBNull(17) ? null : rdr.GetInt64(17),
                UserAgent = rdr.GetString(18),
                ReferenceId = rdr.IsDBNull(19) ? null : rdr.GetString(19),
                ReferenceName = rdr.IsDBNull(20) ? null : rdr.GetString(20)
            };
            list.Add(dto);
        }
        return list;
    }
}
