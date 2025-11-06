using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using System.Security.Cryptography;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Implementation of the API key service
    /// </summary>
    public class ApiKeyService : IApiKeyService
    {
        private readonly ILogger<ApiKeyService> _logger;
        private readonly IApiKeyRepository _apiKeyRepository;

        /// <summary>
        /// Initializes a new instance of the ApiKeyService class
        /// </summary>
        /// <param name="logger">           The logger </param>
        /// <param name="apiKeyRepository"> The API key repository </param>
        public ApiKeyService(
            ILogger<ApiKeyService> logger,
            IApiKeyRepository apiKeyRepository)
        {
            _logger = logger;
            _apiKeyRepository = apiKeyRepository;
        }

        /// <inheritdoc/>
        public async Task<ApiKey> GetByKeyAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return await _apiKeyRepository.GetByKeyAsync(key);
        }

        /// <inheritdoc/>
        public async Task<ApiKey> GetByIdAsync(int id)
        {
            return await _apiKeyRepository.GetByIdAsync(id);
        }

        /// <inheritdoc/>
        public async Task<List<ApiKey>> GetAllAsync()
        {
            return (List<ApiKey>)await _apiKeyRepository.GetAllAsync();
        }

        /// <inheritdoc/>
        public async Task<ApiKey> CreateAsync(string clientName, DateTime? expiresAt = null, string allowedIpAddresses = null)
        {
            if (string.IsNullOrEmpty(clientName))
            {
                throw new ArgumentException("Client name is required", nameof(clientName));
            }

            var apiKey = new ApiKey
            {
                Key = GenerateApiKey(),
                ClientName = clientName,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                IsActive = true,
                AllowedIpAddresses = allowedIpAddresses,
                Version = 1
            };

            return await _apiKeyRepository.AddAsync(apiKey);
        }

        /// <inheritdoc/>
        public async Task<ApiKey> UpdateAsync(ApiKey apiKey)
        {
            if (apiKey == null)
            {
                throw new ArgumentNullException(nameof(apiKey));
            }

            return await _apiKeyRepository.UpdateAsync(apiKey);
        }

        /// <inheritdoc/>
        public async Task<bool> DeactivateAsync(int id)
        {
            var apiKey = await _apiKeyRepository.GetByIdAsync(id);
            if (apiKey == null)
            {
                return false;
            }

            apiKey.IsActive = false;
            await _apiKeyRepository.UpdateAsync(apiKey);
            return true;
        }

        /// <inheritdoc/>
        public async Task<ApiKey> RotateAsync(int id, int gracePeriodDays = 7)
        {
            var apiKey = await _apiKeyRepository.GetByIdAsync(id);
            if (apiKey == null)
            {
                throw new ArgumentException($"API key with ID {id} not found", nameof(id));
            }

            // Store the previous key
            apiKey.PreviousKey = apiKey.Key;
            apiKey.PreviousKeyExpiresAt = DateTime.UtcNow.AddDays(gracePeriodDays);

            // Generate a new key
            apiKey.Key = GenerateApiKey();
            apiKey.Version++;

            return await _apiKeyRepository.UpdateAsync(apiKey);
        }

        /// <inheritdoc/>
        public async Task<ApiKey> ValidateAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            var apiKey = await _apiKeyRepository.GetByKeyAsync(key);
            if (apiKey != null && apiKey.IsValid())
            {
                return apiKey;
            }

            // Check if it matches a previous key in the grace period
            var apiKeyByPrevious = await _apiKeyRepository.GetByPreviousKeyAsync(key);
            if (apiKeyByPrevious != null && apiKeyByPrevious.IsPreviousKeyValid())
            {
                return apiKeyByPrevious;
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task<bool> RecordUsageAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var apiKey = await _apiKeyRepository.GetByKeyAsync(key);
            if (apiKey != null)
            {
                apiKey.LastUsedAt = DateTime.UtcNow;
                await _apiKeyRepository.UpdateAsync(apiKey);
                return true;
            }

            // Check if it's a previous key
            var apiKeyByPrevious = await _apiKeyRepository.GetByPreviousKeyAsync(key);
            if (apiKeyByPrevious != null)
            {
                apiKeyByPrevious.LastUsedAt = DateTime.UtcNow;
                await _apiKeyRepository.UpdateAsync(apiKeyByPrevious);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Generates a new API key
        /// </summary>
        /// <returns> The generated API key </returns>
        private string GenerateApiKey()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}