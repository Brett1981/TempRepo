using Sage200Microservice.Data.Models;
using System.Threading;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository interface for API key operations
    /// </summary>
    public interface IApiKeyRepository : IRepository<ApiKey>
    {
        Task<ApiKey?> GetByKeyAsync(string key, CancellationToken ct = default);
        Task<ApiKey?> GetByPreviousKeyAsync(string previousKey, CancellationToken ct = default);
        Task<bool> IsValidKeyAsync(string key, CancellationToken ct = default);
        Task<bool> UpdateLastUsedAsync(string key, CancellationToken ct = default);

        Task<(IEnumerable<ApiKey> Items, int TotalCount)> GetFilteredPagedAsync(
            string? clientName = null,
            bool? isActive = null,
            bool? isExpired = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "Id",
            string sortDirection = "asc",
            CancellationToken ct = default);

        Task<List<ApiKey>> GetKeysDueForRotationAsync(int maxAgeInDays, CancellationToken ct = default);
        Task<List<ApiKey>> GetKeysWithExpiredPreviousKeysAsync(CancellationToken ct = default);
        Task<int> CleanupExpiredPreviousKeysAsync(CancellationToken ct = default);

        Task<PaginatedResult<ApiKey>> GetAllAsync(
            int page = 1,
            int pageSize = 10,
            string sortBy = "CreatedAt",
            string sortDirection = "desc",
            CancellationToken ct = default);
    }
}
