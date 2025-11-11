using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Sage200Microservice.Services.Auth
{
    public sealed class OAuthStateStore : IOAuthStateStore
    {
        private readonly IDistributedCache _cache;
        private const string Prefix = "oauthstate:";

        public OAuthStateStore(IDistributedCache cache) => _cache = cache;

        public async Task<string> CreateAsync(TimeSpan ttl, CancellationToken ct = default)
        {
            // 128-bit random value as URL-safe Base64
            var bytes = RandomNumberGenerator.GetBytes(16);
            var state = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await _cache.SetStringAsync(Prefix + state, "1", options, ct).ConfigureAwait(false);
            return state;
        }

        public async Task<bool> TryConsumeAsync(string state, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;

            var key = Prefix + state;
            var exists = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);
            if (exists is null) return false;

            await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
            return true;
        }
    }
}
