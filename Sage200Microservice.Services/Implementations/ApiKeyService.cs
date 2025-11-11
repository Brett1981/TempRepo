using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using System.Security.Cryptography;

namespace Sage200Microservice.Services.Implementations
{
    public class ApiKeyService : IApiKeyService
    {
        private readonly ILogger<ApiKeyService> _logger;
        private readonly IApiKeyRepository _apiKeyRepository;

        public ApiKeyService(
            ILogger<ApiKeyService> logger,
            IApiKeyRepository apiKeyRepository)
        {
            _logger = logger;
            _apiKeyRepository = apiKeyRepository;
        }

        public async Task<ApiKey?> GetByKeyAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            // CT-enabled (from your IApiKeyRepository)
            return await _apiKeyRepository.GetByKeyAsync(key, ct);
        }

        public Task<ApiKey?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            // Base IRepository<T> version likely has NO ct — call the non-CT overload.
            return _apiKeyRepository.GetByIdAsync(id);
        }

        public async Task<List<ApiKey>> GetAllAsync(CancellationToken ct = default)
        {
            // Use your paged GetAllAsync(...) that DOES accept a CT and flatten to a list
            var page = await _apiKeyRepository.GetAllAsync(
                page: 1,
                pageSize: 1000,            // adjust if you want a different admin cap
                sortBy: "CreatedAt",
                sortDirection: "desc",
                ct: ct);

            return page.Items.ToList();
        }

        public async Task<ApiKey> CreateAsync(
            string clientName,
            DateTime? expiresAt = null,
            string? allowedIpAddresses = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientName))
                throw new ArgumentException("Client name is required", nameof(clientName));

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

            // Base IRepository<T>.AddAsync(entity) — non-CT
            return await _apiKeyRepository.AddAsync(apiKey);
        }

        public async Task<ApiKey> UpdateAsync(ApiKey apiKey, CancellationToken ct = default)
        {
            if (apiKey is null) throw new ArgumentNullException(nameof(apiKey));
            // Base IRepository<T>.UpdateAsync(entity) — non-CT
            return await _apiKeyRepository.UpdateAsync(apiKey);
        }

        public async Task<bool> DeactivateAsync(int id, CancellationToken ct = default)
        {
            // Non-CT base call
            var apiKey = await _apiKeyRepository.GetByIdAsync(id);
            if (apiKey is null) return false;

            apiKey.IsActive = false;
            // Non-CT base call
            await _apiKeyRepository.UpdateAsync(apiKey);
            return true;
        }

        public async Task<ApiKey?> RotateAsync(int id, int gracePeriodDays = 7, CancellationToken ct = default)
        {
            // Non-CT base call
            var apiKey = await _apiKeyRepository.GetByIdAsync(id);
            if (apiKey is null)
                throw new ArgumentException($"API key with ID {id} not found", nameof(id));

            apiKey.PreviousKey = apiKey.Key;
            apiKey.PreviousKeyExpiresAt = DateTime.UtcNow.AddDays(gracePeriodDays);
            apiKey.Key = GenerateApiKey();
            apiKey.Version++;

            // Non-CT base call
            return await _apiKeyRepository.UpdateAsync(apiKey);
        }

        public async Task<ApiKey?> ValidateAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            // CT-enabled helpers
            var isValid = await _apiKeyRepository.IsValidKeyAsync(key, ct);
            if (!isValid) return null;

            var current = await _apiKeyRepository.GetByKeyAsync(key, ct);
            if (current is not null) return current;

            var previous = await _apiKeyRepository.GetByPreviousKeyAsync(key, ct);
            return previous;
        }

        public async Task<bool> RecordUsageAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            // CT-enabled fast path
            var ok = await _apiKeyRepository.UpdateLastUsedAsync(key, ct);
            if (ok) return true;

            // Fallback: load (CT-enabled), update (non-CT)
            var entity = await _apiKeyRepository.GetByKeyAsync(key, ct)
                        ?? await _apiKeyRepository.GetByPreviousKeyAsync(key, ct);

            if (entity is null) return false;

            entity.LastUsedAt = DateTime.UtcNow;
            await _apiKeyRepository.UpdateAsync(entity); // non-CT base
            return true;
        }

        private static string GenerateApiKey()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var base64 = Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            return base64;
        }
    }
}
