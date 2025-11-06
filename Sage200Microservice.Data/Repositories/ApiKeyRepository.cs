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
        /// <summary>
        /// Initializes a new instance of the ApiKeyRepository class
        /// </summary>
        /// <param name="context"> The database context </param>
        public ApiKeyRepository(ApplicationContext context) : base(context)
        {
        }

        /// <summary>
        /// Gets an API key by its key value
        /// </summary>
        /// <param name="key"> The API key value </param>
        /// <returns> The API key if found, null otherwise </returns>
        public async Task<ApiKey> GetByKeyAsync(string key)
        {
            return await _context.ApiKeys
                .FirstOrDefaultAsync(k => k.Key == key);
        }

        /// <summary>
        /// Gets an API key by its previous key value
        /// </summary>
        /// <param name="previousKey"> The previous API key value </param>
        /// <returns> The API key if found, null otherwise </returns>
        public async Task<ApiKey> GetByPreviousKeyAsync(string previousKey)
        {
            return await _context.ApiKeys
                .FirstOrDefaultAsync(k => k.PreviousKey == previousKey);
        }

        /// <summary>
        /// Validates an API key
        /// </summary>
        /// <param name="key"> The API key value to validate </param>
        /// <returns> True if the key is valid, false otherwise </returns>
        public async Task<bool> IsValidKeyAsync(string key)
        {
            var apiKey = await GetByKeyAsync(key);

            if (apiKey == null)
            {
                // Check if it's a previous key in the grace period
                var apiKeyByPrevious = await GetByPreviousKeyAsync(key);
                if (apiKeyByPrevious != null && apiKeyByPrevious.IsPreviousKeyValid())
                {
                    return true;
                }

                return false;
            }

            return apiKey.IsValid();
        }

        /// <summary>
        /// Updates the last used timestamp for an API key
        /// </summary>
        /// <param name="key"> The API key value </param>
        /// <returns> True if the update was successful, false otherwise </returns>
        public async Task<bool> UpdateLastUsedAsync(string key)
        {
            var apiKey = await GetByKeyAsync(key);

            if (apiKey == null)
            {
                // Check if it's a previous key
                var apiKeyByPrevious = await GetByPreviousKeyAsync(key);
                if (apiKeyByPrevious == null)
                {
                    return false;
                }

                apiKeyByPrevious.LastUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return true;
            }

            apiKey.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Gets a filtered and paginated list of API keys
        /// </summary>
        /// <param name="clientName">    Filter by client name </param>
        /// <param name="isActive">      Filter by active status </param>
        /// <param name="isExpired">     Filter by expiration status </param>
        /// <param name="startDate">     Filter by creation date (start) </param>
        /// <param name="endDate">       Filter by creation date (end) </param>
        /// <param name="page">          The page number (1-based) </param>
        /// <param name="pageSize">      The number of items per page </param>
        /// <param name="sortBy">        The property name to sort by </param>
        /// <param name="sortDirection"> The sort direction (asc or desc) </param>
        /// <returns> A filtered and paginated list of API keys </returns>
        public async Task<(IEnumerable<ApiKey> Items, int TotalCount)> GetFilteredPagedAsync(
            string clientName = null,
            bool? isActive = null,
            bool? isExpired = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "Id",
            string sortDirection = "asc")
        {
            // Start with all API keys
            var query = _context.ApiKeys.AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                query = query.Where(k => k.ClientName.Contains(clientName));
            }

            if (isActive.HasValue)
            {
                query = query.Where(k => k.IsActive == isActive.Value);
            }

            if (isExpired.HasValue)
            {
                var now = DateTime.UtcNow;
                if (isExpired.Value)
                {
                    // Expired keys have an expiration date in the past
                    query = query.Where(k => k.ExpiresAt.HasValue && k.ExpiresAt.Value < now);
                }
                else
                {
                    // Non-expired keys either have no expiration date or a future expiration date
                    query = query.Where(k => !k.ExpiresAt.HasValue || k.ExpiresAt.Value >= now);
                }
            }

            if (startDate.HasValue)
            {
                query = query.Where(k => k.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // Include the entire end date (up to 23:59:59)
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(k => k.CreatedAt <= endOfDay);
            }

            // Get the total count before pagination
            var totalCount = await query.CountAsync();

            // Apply sorting and pagination
            var items = await query
                .ApplySorting(sortBy, sortDirection)
                .ApplyPaging(page, pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        /// <summary>
        /// Gets API keys that are due for rotation based on age
        /// </summary>
        /// <param name="maxAgeInDays"> The maximum age in days before a key should be rotated </param>
        /// <returns> A list of API keys due for rotation </returns>
        public async Task<List<ApiKey>> GetKeysDueForRotationAsync(int maxAgeInDays)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeInDays);

            return await _context.ApiKeys
                .Where(k => k.IsActive &&
                           (k.CreatedAt < cutoffDate ||
                            (k.Version > 1 && k.PreviousKeyExpiresAt.HasValue && k.PreviousKeyExpiresAt.Value < cutoffDate)))
                .ToListAsync();
        }

        /// <summary>
        /// Gets API keys with expired previous keys that need cleanup
        /// </summary>
        /// <returns> A list of API keys with expired previous keys </returns>
        public async Task<List<ApiKey>> GetKeysWithExpiredPreviousKeysAsync()
        {
            var now = DateTime.UtcNow;

            return await _context.ApiKeys
                .Where(k => !string.IsNullOrEmpty(k.PreviousKey) &&
                           k.PreviousKeyExpiresAt.HasValue &&
                           k.PreviousKeyExpiresAt.Value < now)
                .ToListAsync();
        }

        /// <summary>
        /// Cleans up expired previous keys
        /// </summary>
        /// <returns> The number of keys cleaned up </returns>
        public async Task<int> CleanupExpiredPreviousKeysAsync()
        {
            var keysToCleanup = await GetKeysWithExpiredPreviousKeysAsync();

            foreach (var key in keysToCleanup)
            {
                key.PreviousKey = null;
                key.PreviousKeyExpiresAt = null;
            }

            await _context.SaveChangesAsync();

            return keysToCleanup.Count;
        }

        public async Task<PaginatedResult<ApiKey>> GetAllAsync(
            int page = 1,
            int pageSize = 10,
            string sortBy = "CreatedAt",
            string sortDirection = "desc")
        {
            var query = _context.ApiKeys.AsNoTracking();

            // sorting
            bool desc = sortDirection?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            query = (sortBy?.ToLowerInvariant()) switch
            {
                "clientname" => (desc ? query.OrderByDescending(x => x.ClientName) : query.OrderBy(x => x.ClientName)),
                "expiresat" => (desc ? query.OrderByDescending(x => x.ExpiresAt) : query.OrderBy(x => x.ExpiresAt)),
                "createdat" or _ => (desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)),
            };

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PaginatedResult<ApiKey>(items, total, page, pageSize);
        }
    }
}