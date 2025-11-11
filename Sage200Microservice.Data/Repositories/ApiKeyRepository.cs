using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Extensions;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository implementation for API key operations
    /// </summary>
    public class ApiKeyRepository : Repository<ApiKey>, IApiKeyRepository
    {
        public ApiKeyRepository(ApplicationContext context) : base(context) { }

        public async Task<ApiKey?> GetByKeyAsync(string key, CancellationToken ct = default)
        {
            return await _context.ApiKeys
                .FirstOrDefaultAsync(k => k.Key == key, ct);
        }

        public async Task<ApiKey?> GetByPreviousKeyAsync(string previousKey, CancellationToken ct = default)
        {
            return await _context.ApiKeys
                .FirstOrDefaultAsync(k => k.PreviousKey == previousKey, ct);
        }

        public async Task<bool> IsValidKeyAsync(string key, CancellationToken ct = default)
        {
            // Try current key
            var apiKey = await GetByKeyAsync(key, ct);
            if (apiKey != null)
                return apiKey.IsValid();

            // Try previous (within grace period)
            var apiKeyByPrevious = await GetByPreviousKeyAsync(key, ct);
            if (apiKeyByPrevious != null && apiKeyByPrevious.IsPreviousKeyValid())
                return true;

            return false;
        }

        public async Task<bool> UpdateLastUsedAsync(string key, CancellationToken ct = default)
        {
            // Prefer current key
            var apiKey = await GetByKeyAsync(key, ct);
            if (apiKey == null)
            {
                // Fallback to previous key
                var previous = await GetByPreviousKeyAsync(key, ct);
                if (previous == null) return false;

                previous.LastUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                return true;
            }

            apiKey.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<(IEnumerable<ApiKey> Items, int TotalCount)> GetFilteredPagedAsync(
            string? clientName = null,
            bool? isActive = null,
            bool? isExpired = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "Id",
            string sortDirection = "asc",
            CancellationToken ct = default)
        {
            var query = _context.ApiKeys.AsQueryable();

            // Filters
            if (!string.IsNullOrWhiteSpace(clientName))
                query = query.Where(k => k.ClientName.Contains(clientName));

            if (isActive.HasValue)
                query = query.Where(k => k.IsActive == isActive.Value);

            if (isExpired.HasValue)
            {
                var now = DateTime.UtcNow;
                query = isExpired.Value
                    ? query.Where(k => k.ExpiresAt.HasValue && k.ExpiresAt.Value < now)
                    : query.Where(k => !k.ExpiresAt.HasValue || k.ExpiresAt.Value >= now);
            }

            if (startDate.HasValue)
                query = query.Where(k => k.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(k => k.CreatedAt <= endOfDay);
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .ApplySorting(sortBy, sortDirection)
                .ApplyPaging(page, pageSize)
                .ToListAsync(ct);

            return (items, totalCount);
        }

        public async Task<List<ApiKey>> GetKeysDueForRotationAsync(int maxAgeInDays, CancellationToken ct = default)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeInDays);

            return await _context.ApiKeys
                .Where(k => k.IsActive &&
                            (k.CreatedAt < cutoffDate ||
                             (k.Version > 1 && k.PreviousKeyExpiresAt.HasValue && k.PreviousKeyExpiresAt.Value < cutoffDate)))
                .ToListAsync(ct);
        }

        public async Task<List<ApiKey>> GetKeysWithExpiredPreviousKeysAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            return await _context.ApiKeys
                .Where(k => !string.IsNullOrEmpty(k.PreviousKey) &&
                            k.PreviousKeyExpiresAt.HasValue &&
                            k.PreviousKeyExpiresAt.Value < now)
                .ToListAsync(ct);
        }

        public async Task<int> CleanupExpiredPreviousKeysAsync(CancellationToken ct = default)
        {
            var keysToCleanup = await GetKeysWithExpiredPreviousKeysAsync(ct);

            foreach (var key in keysToCleanup)
            {
                key.PreviousKey = null;
                key.PreviousKeyExpiresAt = null;
            }

            await _context.SaveChangesAsync(ct);
            return keysToCleanup.Count;
        }

        public async Task<PaginatedResult<ApiKey>> GetAllAsync(
            int page = 1,
            int pageSize = 10,
            string sortBy = "CreatedAt",
            string sortDirection = "desc",
            CancellationToken ct = default)
        {
            var query = _context.ApiKeys.AsNoTracking();

            bool desc = sortDirection?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            query = (sortBy?.ToLowerInvariant()) switch
            {
                "clientname" => (desc ? query.OrderByDescending(x => x.ClientName) : query.OrderBy(x => x.ClientName)),
                "expiresat" => (desc ? query.OrderByDescending(x => x.ExpiresAt) : query.OrderBy(x => x.ExpiresAt)),
                "createdat" or _ => (desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)),
            };

            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PaginatedResult<ApiKey>(items, total, page, pageSize);
        }
    }
}
