using Microsoft.Extensions.Caching.Memory;

namespace Sage200Microservice.API.Services
{
    /// <summary>
    /// Service for caching data
    /// </summary>
    public class CachingService : ICachingService
    {
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Initializes a new instance of the CachingService class
        /// </summary>
        /// <param name="cache"> The memory cache </param>
        public CachingService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public Task<T?> GetAsync<T>(string key)
        {
            _ = _cache.TryGetValue(key, out T value);
            return Task.FromResult<T?>(value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan absoluteExpiration)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpiration
            };
            _cache.Set(key, value, options);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets a value from the cache or creates it if it doesn't exist
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">                       The cache key </param>
        /// <param name="factory">                   The factory function to create the value </param>
        /// <param name="absoluteExpirationMinutes"> The absolute expiration time in minutes </param>
        /// <param name="slidingExpirationMinutes">  The sliding expiration time in minutes </param>
        /// <returns> The cached value </returns>
        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            int absoluteExpirationMinutes = 60,
            int slidingExpirationMinutes = 20)
        {
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(absoluteExpirationMinutes);
                entry.SlidingExpiration = TimeSpan.FromMinutes(slidingExpirationMinutes);

                return await factory();
            });
        }

        /// <summary>
        /// Gets a value from the cache
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">   The cache key </param>
        /// <param name="value"> The cached value </param>
        /// <returns> True if the value was found in the cache, false otherwise </returns>
        public bool TryGetValue<T>(string key, out T value)
        {
            return _cache.TryGetValue(key, out value);
        }

        /// <summary>
        /// Sets a value in the cache
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">                       The cache key </param>
        /// <param name="value">                     The value to cache </param>
        /// <param name="absoluteExpirationMinutes"> The absolute expiration time in minutes </param>
        /// <param name="slidingExpirationMinutes">  The sliding expiration time in minutes </param>
        public void Set<T>(
            string key,
            T value,
            int absoluteExpirationMinutes = 60,
            int slidingExpirationMinutes = 20)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(absoluteExpirationMinutes),
                SlidingExpiration = TimeSpan.FromMinutes(slidingExpirationMinutes)
            };

            _cache.Set(key, value, options);
        }

        /// <summary>
        /// Removes a value from the cache
        /// </summary>
        /// <param name="key"> The cache key </param>
        public void Remove(string key)
        {
            _cache.Remove(key);
        }
    }

    /// <summary>
    /// Interface for caching service
    /// </summary>
    public interface ICachingService
    {
        Task<T?> GetAsync<T>(string key);

        Task SetAsync<T>(string key, T value, TimeSpan absoluteExpiration);

        /// <summary>
        /// Gets a value from the cache or creates it if it doesn't exist
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">                       The cache key </param>
        /// <param name="factory">                   The factory function to create the value </param>
        /// <param name="absoluteExpirationMinutes"> The absolute expiration time in minutes </param>
        /// <param name="slidingExpirationMinutes">  The sliding expiration time in minutes </param>
        /// <returns> The cached value </returns>
        Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            int absoluteExpirationMinutes = 60,
            int slidingExpirationMinutes = 20);

        /// <summary>
        /// Gets a value from the cache
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">   The cache key </param>
        /// <param name="value"> The cached value </param>
        /// <returns> True if the value was found in the cache, false otherwise </returns>
        bool TryGetValue<T>(string key, out T value);

        /// <summary>
        /// Sets a value in the cache
        /// </summary>
        /// <typeparam name="T"> The type of the value </typeparam>
        /// <param name="key">                       The cache key </param>
        /// <param name="value">                     The value to cache </param>
        /// <param name="absoluteExpirationMinutes"> The absolute expiration time in minutes </param>
        /// <param name="slidingExpirationMinutes">  The sliding expiration time in minutes </param>
        void Set<T>(
            string key,
            T value,
            int absoluteExpirationMinutes = 60,
            int slidingExpirationMinutes = 20);

        /// <summary>
        /// Removes a value from the cache
        /// </summary>
        /// <param name="key"> The cache key </param>
        void Remove(string key);
    }
}