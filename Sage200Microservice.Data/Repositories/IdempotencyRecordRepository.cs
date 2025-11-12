using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories;

/// <summary>
/// EF Core repository for idempotency records.
/// </summary>
public sealed class IdempotencyRecordRepository : IIdempotencyRecordRepository
{
    private readonly ApplicationContext _db;

    public IdempotencyRecordRepository(ApplicationContext db) => _db = db;

    /// <inheritdoc/>
    public Task<IdempotencyRecord?> GetByKeyHashAsync(string keyHash, CancellationToken ct = default)
        => _db.Set<IdempotencyRecord>()
              .AsNoTracking()
              .FirstOrDefaultAsync(x => x.KeyHash == keyHash, ct)!;

    /// <inheritdoc/>
    public async Task SaveAsync(IdempotencyRecord rec, CancellationToken ct = default)
    {
        if (rec.Id == 0)
            _db.Add(rec);
        else
            _db.Update(rec);

        await _db.SaveChangesAsync(ct);
    }

    // Produces Base64(SHA-512) ~88 chars (fits NVARCHAR(88) KeyHash)
    private static string HashKeySha512Base64(string key)
    {
        using var sha = System.Security.Cryptography.SHA512.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(key ?? string.Empty);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public async Task UpsertResultUrnAsync(
        string idempotencyKey,
        string? resource,
        string resultSageUrn,
        DateTime? expiresUtc,
        CancellationToken ct = default)
    {
        var keyHash = HashKeySha512Base64(idempotencyKey);

        var rec = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(x => x.KeyHash == keyHash, ct)
            .ConfigureAwait(false);

        if (rec is null)
        {
            rec = new IdempotencyRecord
            {
                KeyHash = keyHash,
                CreatedUtc = DateTime.UtcNow,
                // RequestHash/ResourceId are optional in your schema
                ResultSageUrn = resultSageUrn,
                ExpiresUtc = expiresUtc
            };
            _db.IdempotencyRecords.Add(rec);
        }
        else if (string.IsNullOrWhiteSpace(rec.ResultSageUrn))
        {
            // First-writer-wins: only set if not already present
            rec.ResultSageUrn = resultSageUrn;
            if (expiresUtc.HasValue) rec.ExpiresUtc = expiresUtc;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertHttpResponseAsync(
        string idempotencyKey, string? resource,
        int statusCode, string? contentType, string headersJson, string? body,
        string? requestHash, DateTime? expiresUtc, CancellationToken ct = default)
    {
        var keyHash = HashKeySha512Base64(idempotencyKey);
        var rec = await _db.IdempotencyRecords.FirstOrDefaultAsync(x => x.KeyHash == keyHash, ct);
        if (rec is null)
        {
            rec = new IdempotencyRecord
            {
                KeyHash = keyHash,
                CreatedUtc = DateTime.UtcNow,
                Resource = resource,
                RequestHash = requestHash,
                ResponseStatusCode = statusCode,
                ResponseContentType = contentType,
                ResponseHeaders = headersJson,
                ResponseBody = body,
                ExpiresUtc = expiresUtc
            };
            _db.IdempotencyRecords.Add(rec);
        }
        else if (rec.ResponseStatusCode is null) // first-writer-wins
        {
            rec.Resource ??= resource;
            rec.RequestHash ??= requestHash;
            rec.ResponseStatusCode = statusCode;
            rec.ResponseContentType = contentType;
            rec.ResponseHeaders = headersJson;
            rec.ResponseBody = body;
            if (expiresUtc.HasValue) rec.ExpiresUtc = expiresUtc;
        }
        await _db.SaveChangesAsync(ct);
    }
}