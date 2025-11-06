using System;
using System.Collections.Generic;

namespace Sage200Microservice.Services.Logging
{
    // Minimal POCO for ApiLogs insert (keeps us decoupled from EF entity classes)
    public sealed class ApiLogRecord
    {
        public string Endpoint { get; init; } = "";
        public string RequestMethod { get; init; } = "";
        public string? RequestPayloadEncrypted { get; init; }
        public string? ResponsePayloadEncrypted { get; init; }
        public int HttpStatusCode { get; init; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public string? CallerId { get; init; }
        public string ApiType { get; init; } = "Sage200.SOP";

        // Optional: for diagnostics
        public string? RequestHeadersEncrypted { get; init; }
        public string? ResponseHeadersEncrypted { get; init; }
        public string? CorrelationId { get; init; }
        public long? DurationMs { get; init; }
    }
}
