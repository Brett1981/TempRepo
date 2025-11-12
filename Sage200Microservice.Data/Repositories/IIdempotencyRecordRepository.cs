using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories;

/// <summary>
/// Abstraction for idempotency record access.
/// </summary>
public interface IIdempotencyRecordRepository
{
    Task<IdempotencyRecord?> GetByKeyHashAsync(string keyHash, CancellationToken ct = default);

    Task SaveAsync(IdempotencyRecord rec, CancellationToken ct = default);

    /// <summary>
    /// Upserts the ResultSageUrn for a given idempotency key hash (and optional resource), creating
    /// the record if it does not exist. Does not throw if the record already exists.
    /// </summary>
    Task UpsertResultUrnAsync(string idempotencyKey, string? resource, string resultSageUrn, DateTime? expiresUtc, CancellationToken ct = default);
    Task UpsertHttpResponseAsync(
        string idempotencyKey, string? resource,
        int statusCode, string? contentType, string headersJson, string? body,
        string? requestHash, DateTime? expiresUtc, CancellationToken ct = default);
}