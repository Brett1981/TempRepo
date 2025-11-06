namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Immutable struct holding essential context for service calls,
    /// abstracting away the source (HTTP or Kafka).
    /// </summary>
    public readonly record struct RequestContext(
        string SiteId,        // Required, resolved upstream
        string CompanyId,     // Required, resolved upstream
        string? IdempotencyKey, // Optional
        string CorrelationId  // Required (TraceIdentifier or Kafka header)
    );
}