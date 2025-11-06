namespace Sage200Microservice.Services.Logging;

public interface IDbLogReader
{
    Task<IReadOnlyList<ApiLogDto>> GetApiLogsAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(int skip, int take, CancellationToken ct);
}

public sealed class ApiLogDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Endpoint { get; set; } = "";
    public string RequestMethod { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public string CallerId { get; set; } = "";
    public string ApiType { get; set; } = "";
    public bool RequestEncrypted { get; set; }
    public bool ResponseEncrypted { get; set; }
    public string RequestPayload { get; set; } = "";
    public string ResponsePayload { get; set; } = "";
}

public sealed class AuditLogDto
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int EventType { get; set; }
    public int Category { get; set; }
    public int Severity { get; set; }
    public string? UserId { get; set; }
    public string? ClientId { get; set; }
    public string IpAddress { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Action { get; set; } = "";
    public int Status { get; set; }
    public string Description { get; set; } = "";
    public string Details { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string UrlPath { get; set; } = "";
    public int? HttpStatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string UserAgent { get; set; } = "";
    public string? ReferenceId { get; set; }
    public string? ReferenceName { get; set; }
}
