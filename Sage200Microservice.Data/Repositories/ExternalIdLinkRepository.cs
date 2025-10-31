using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// EF Core implementation for <see cref="IExternalIdLinkRepository"/>.
    /// </summary>
    public sealed class ExternalIdLinkRepository : IExternalIdLinkRepository
    {
        private readonly ApplicationContext _db;

        /// <summary>
        /// Initializes a new instance of the repository.
        /// </summary>
        public ExternalIdLinkRepository(ApplicationContext db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public async Task<bool> TryInsertAsync(ExternalIdLink link, CancellationToken ct = default)
        {
            // Defensive guard: enum must be one of the closed set.
            if (!Enum.IsDefined(typeof(ExternalEntityType), link.EntityType))
                throw new ArgumentOutOfRangeException(nameof(link.EntityType), "Unsupported entity type.");

            if (string.IsNullOrWhiteSpace(link.ExternalRef))
                throw new ArgumentException("ExternalRef is required.", nameof(link));

            // Enforce CHECK intent at the app layer too.
            var hasAnySageId = (link.SageId != null) || !string.IsNullOrWhiteSpace(link.SageUrn);
            if (!hasAnySageId)
                throw new ArgumentException("Either SageId or SageUrn must be provided.", nameof(link));

            // Fail fast if AppId doesn’t exist (better message than raw FK error).
            var appExists = await _db.Set<ApiKey>()
                                     .AsNoTracking()
                                     .AnyAsync(k => k.Id == link.AppId, ct);
            if (!appExists)
                throw new InvalidOperationException($"Unknown AppId={link.AppId} (no matching ApiKeys.Id). " +
                    "Resolve AppId from the 'X-Api-Key' header before inserting the link.");

            // Look for an existing mapping by unique key.
            var existing = await _db.ExternalIdLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.AppId == link.AppId &&
                    x.EntityType == link.EntityType &&
                    x.ExternalRef == link.ExternalRef, ct);

            if (existing != null)
            {
                // Identical mapping → no-op (idempotent)
                var sameId = existing.SageId == link.SageId;
                var sameUrn = string.Equals(existing.SageUrn, link.SageUrn, StringComparison.Ordinal);

                if (sameId && sameUrn)
                    return false; // no-op

                // Conflict on canonical identifier
                throw new InvalidOperationException(
                    $"ExternalIdLink conflict for (AppId={link.AppId}, EntityType={link.EntityType}, ExternalRef={link.ExternalRef}). " +
                    $"existingSageId={existing.SageId}, existingSageUrn={existing.SageUrn}, requestedSageId={link.SageId}, requestedSageUrn={link.SageUrn}");
            }

            link.CreatedUtc = DateTime.UtcNow;
            _db.ExternalIdLinks.Add(link);
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // Unique index collision (concurrent insert). Re-read and decide.
                var raced = await _db.ExternalIdLinks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.AppId == link.AppId
                                           && x.EntityType == link.EntityType
                                           && x.ExternalRef == link.ExternalRef, ct);
                if (raced is null)
                    throw; // unexpected

                var requiresId = link.EntityType == ExternalEntityType.Customer || link.EntityType == ExternalEntityType.SopOrder;
                var requiresUrn = !requiresId;
                var sameId = raced.SageId == link.SageId;
                var sameUrn = string.Equals(raced.SageUrn, link.SageUrn, System.StringComparison.Ordinal);
                if ((requiresId && sameId) || (requiresUrn && sameUrn))
                    return false; // idempotent under race

                var msg = $"ExternalIdLink conflict for (AppId={link.AppId}, EntityType={link.EntityType}, ExternalRef={link.ExternalRef}). " +
                              $"existingSageId={raced.SageId}, existingSageUrn={raced.SageUrn}, requestedSageId={link.SageId}, requestedSageUrn={link.SageUrn}";
                throw new InvalidOperationException(msg);
            }
        }

        /// <inheritdoc/>
        public Task<ExternalIdLink?> FindByExternalAsync(int appId, ExternalEntityType entityType, string externalRef, CancellationToken ct = default)
        {
            return _db.ExternalIdLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.AppId == appId &&
                    x.EntityType == entityType &&
                    x.ExternalRef == externalRef, ct);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ExternalIdLink>> ListBySageIdAsync(ExternalEntityType entityType, long sageId, int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var skip = Math.Max(0, (page - 1)) * Math.Clamp(pageSize, 1, 100);
            var take = Math.Clamp(pageSize, 1, 100);

            return await _db.ExternalIdLinks
                .AsNoTracking()
                .Where(x => x.EntityType == entityType && x.SageId == sageId)
                .OrderBy(x => x.AppId).ThenBy(x => x.ExternalRef)
                .Skip(skip).Take(take)
                .ToListAsync(ct);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ExternalIdLink>> ListBySageUrnAsync(ExternalEntityType entityType, string sageUrn, int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var skip = Math.Max(0, (page - 1)) * Math.Clamp(pageSize, 1, 100);
            var take = Math.Clamp(pageSize, 1, 100);

            return await _db.ExternalIdLinks
                .AsNoTracking()
                .Where(x => x.EntityType == entityType && x.SageUrn == sageUrn)
                .OrderBy(x => x.AppId).ThenBy(x => x.ExternalRef)
                .Skip(skip).Take(take)
                .ToListAsync(ct);
        }
    }
}