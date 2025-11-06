using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository interface for API key operations
    /// </summary>
    public interface IApiKeyRepository : IRepository<ApiKey>
    {
        /// <summary>
        /// Gets an API key by its key value
        /// </summary>
        /// <param name="key"> The API key value </param>
        /// <returns> The API key if found, null otherwise </returns>
        Task<ApiKey> GetByKeyAsync(string key);

        /// <summary>
        /// Gets an API key by its previous key value
        /// </summary>
        /// <param name="previousKey"> The previous API key value </param>
        /// <returns> The API key if found, null otherwise </returns>
        Task<ApiKey> GetByPreviousKeyAsync(string previousKey);

        /// <summary>
        /// Validates an API key
        /// </summary>
        /// <param name="key"> The API key value to validate </param>
        /// <returns> True if the key is valid, false otherwise </returns>
        Task<bool> IsValidKeyAsync(string key);

        /// <summary>
        /// Updates the last used timestamp for an API key
        /// </summary>
        /// <param name="key"> The API key value </param>
        /// <returns> True if the update was successful, false otherwise </returns>
        Task<bool> UpdateLastUsedAsync(string key);

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
        Task<(IEnumerable<ApiKey> Items, int TotalCount)> GetFilteredPagedAsync(
            string clientName = null,
            bool? isActive = null,
            bool? isExpired = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 10,
            string sortBy = "Id",
            string sortDirection = "asc");

        /// <summary>
        /// Gets API keys that are due for rotation based on age
        /// </summary>
        /// <param name="maxAgeInDays"> The maximum age in days before a key should be rotated </param>
        /// <returns> A list of API keys due for rotation </returns>
        Task<List<ApiKey>> GetKeysDueForRotationAsync(int maxAgeInDays);

        /// <summary>
        /// Gets API keys with expired previous keys that need cleanup
        /// </summary>
        /// <returns> A list of API keys with expired previous keys </returns>
        Task<List<ApiKey>> GetKeysWithExpiredPreviousKeysAsync();

        /// <summary>
        /// Cleans up expired previous keys
        /// </summary>
        /// <returns> The number of keys cleaned up </returns>
        Task<int> CleanupExpiredPreviousKeysAsync();

        Task<PaginatedResult<ApiKey>> GetAllAsync(
            int page = 1,
            int pageSize = 10,
            string sortBy = "CreatedAt",
            string sortDirection = "desc");
    }
}