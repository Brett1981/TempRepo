// File: Services/Logging/Records/AuditLogRecord.cs
// Mirrors dbo.AuditLogs (types aligned to schema)

using System;

namespace Sage200Microservice.Services.Logging
{
    public sealed class AuditLogRecord
    {
        // DB: datetime2 (we store UTC)
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

        // DB: int (NOT nvarchar)
        public int EventType { get; init; }            // e.g., 0=Business, 1=Security, etc.
        public int Category { get; init; }             // e.g., 0=API, 1=Sage200, ...
        public int Severity { get; init; }             // e.g., 0=Info, 1=Warning, 2=Error, 3=Critical
        public int Status { get; init; }               // e.g., 0=Success, 1=ClientError, 2=ServerError

        // DB: nvarchar(100) NULL
        public string? UserId { get; init; }
        public string? ClientId { get; init; }

        // DB: nvarchar(45) NOT NULL (ipv4/ipv6)
        public string IpAddress { get; init; } = "";

        // DB: nvarchar(100) NOT NULL
        public string Resource { get; init; } = "";

        // DB: nvarchar(100) NOT NULL
        public string Action { get; init; } = "";

        // DB: nvarchar(max) NOT NULL
        public string Description { get; init; } = "";

        // DB: nvarchar(max) NOT NULL (non-sensitive JSON)
        public string Details { get; init; } = "{}";

        // DB: nvarchar(64) NOT NULL
        public string CorrelationId { get; init; } = "";

        // DB: nvarchar(10) NOT NULL
        public string HttpMethod { get; init; } = "GET";

        // DB: nvarchar(2048) NOT NULL
        public string UrlPath { get; init; } = "/";

        // DB: int NULL
        public int? HttpStatusCode { get; init; }

        // DB: bigint NULL
        public long? DurationMs { get; init; }

        // DB: nvarchar(512) NOT NULL
        public string UserAgent { get; init; } = "";

        // DB: nvarchar(max) NULL
        public string? ReferenceId { get; init; }
        public string? ReferenceName { get; init; }

        // DB: nvarchar(max) NULL (redact as needed)
        public string? PreviousState { get; init; }
        public string? NewState { get; init; }

        // DB: int NOT NULL / datetime2 NULL
        public int RetentionDays { get; init; } = 365;
        public DateTime? ExpiresAtUtc { get; init; }
    }
}
