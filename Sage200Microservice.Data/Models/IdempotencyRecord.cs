namespace Sage200Microservice.Data.Models
{
    public sealed class IdempotencyRecord
    {
        public int Id { get; set; }
        public string KeyHash { get; set; } = default!;
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }

        // Legacy/analytics
        public string? ResultSageUrn { get; set; }

        // NEW: full HTTP replay support
        public long? ResourceId { get; set; }
        public string? Resource { get; set; }            // e.g. "api/salesinvoices"
        public string? RequestHash { get; set; }         // SHA-256 HEX of canonical JSON
        public int? ResponseStatusCode { get; set; }
        public string? ResponseContentType { get; set; }
        public string? ResponseHeaders { get; set; }     // JSON (string -> string[])
        public string? ResponseBody { get; set; }
    }
}