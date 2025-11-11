using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service for API key management
    /// </summary>
    public interface IApiKeyService
    {
        Task<ApiKey?> GetByKeyAsync(string key, CancellationToken ct = default);

        Task<ApiKey?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Returns all API keys (admin view). If your repo pages, this should aggregate or be used for small datasets only.</summary>
        Task<List<ApiKey>> GetAllAsync(CancellationToken ct = default);

        Task<ApiKey> CreateAsync(string clientName, DateTime? expiresAt = null, string? allowedIpAddresses = null, CancellationToken ct = default);

        Task<ApiKey> UpdateAsync(ApiKey apiKey, CancellationToken ct = default);

        Task<bool> DeactivateAsync(int id, CancellationToken ct = default);

        Task<ApiKey?> RotateAsync(int id, int gracePeriodDays = 7, CancellationToken ct = default);

        /// <summary>Returns the ApiKey entity if the supplied key is valid (current or previous-in-grace); otherwise null.</summary>
        Task<ApiKey?> ValidateAsync(string key, CancellationToken ct = default);

        /// <summary>Records usage (LastUsedAt). Returns true if a matching current/previous key was updated.</summary>
        Task<bool> RecordUsageAsync(string key, CancellationToken ct = default);
    }
}
