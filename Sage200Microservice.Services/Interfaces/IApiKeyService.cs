using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service for API key management
    /// </summary>
    public interface IApiKeyService
    {
        /// <summary>
        /// Gets an API key by its key value
        /// </summary>
        /// <param name="key"> The key value </param>
        /// <returns> The API key, or null if not found </returns>
        Task<ApiKey> GetByKeyAsync(string key);

        /// <summary>
        /// Gets an API key by its ID
        /// </summary>
        /// <param name="id"> The API key ID </param>
        /// <returns> The API key, or null if not found </returns>
        Task<ApiKey> GetByIdAsync(int id);

        /// <summary>
        /// Gets all API keys
        /// </summary>
        /// <returns> A list of API keys </returns>
        Task<List<ApiKey>> GetAllAsync();

        /// <summary>
        /// Creates a new API key
        /// </summary>
        /// <param name="clientName">         The client name </param>
        /// <param name="expiresAt">          The expiration date (optional) </param>
        /// <param name="allowedIpAddresses">
        /// Comma-separated list of allowed IP addresses or CIDR ranges (optional)
        /// </param>
        /// <returns> The created API key </returns>
        Task<ApiKey> CreateAsync(string clientName, DateTime? expiresAt = null, string allowedIpAddresses = null);

        /// <summary>
        /// Updates an API key
        /// </summary>
        /// <param name="apiKey"> The API key to update </param>
        /// <returns> The updated API key </returns>
        Task<ApiKey> UpdateAsync(ApiKey apiKey);

        /// <summary>
        /// Deactivates an API key
        /// </summary>
        /// <param name="id"> The API key ID </param>
        /// <returns> True if successful, false otherwise </returns>
        Task<bool> DeactivateAsync(int id);

        /// <summary>
        /// Rotates an API key
        /// </summary>
        /// <param name="id">              The API key ID </param>
        /// <param name="gracePeriodDays"> The number of days the old key remains valid </param>
        /// <returns> The updated API key with the new key value </returns>
        Task<ApiKey> RotateAsync(int id, int gracePeriodDays = 7);

        /// <summary>
        /// Validates an API key
        /// </summary>
        /// <param name="key"> The key value </param>
        /// <returns> The API key if valid, null otherwise </returns>
        Task<ApiKey> ValidateAsync(string key);

        /// <summary>
        /// Records usage of an API key
        /// </summary>
        /// <param name="key"> The key value </param>
        /// <returns> True if successful, false otherwise </returns>
        Task<bool> RecordUsageAsync(string key);
    }
}