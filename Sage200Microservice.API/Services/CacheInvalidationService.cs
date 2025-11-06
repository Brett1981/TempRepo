namespace Sage200Microservice.API.Services
{
    /// <summary>
    /// Service for invalidating cache entries
    /// </summary>
    public class CacheInvalidationService : ICacheInvalidationService
    {
        private readonly ICachingService _cachingService;
        private readonly ILogger<CacheInvalidationService> _logger;

        // Cache key patterns for different entity types
        private static readonly Dictionary<string, List<string>> _entityCachePatterns = new Dictionary<string, List<string>>
        {
            { "ApiKey", new List<string> { "apikey_", "apikeys_" } },
            { "Customer", new List<string> { "customer_", "customers_" } },
            { "Invoice", new List<string> { "invoice_", "invoices_" } }
        };

        /// <summary>
        /// Initializes a new instance of the CacheInvalidationService class
        /// </summary>
        /// <param name="cachingService"> The caching service </param>
        /// <param name="logger">         The logger </param>
        public CacheInvalidationService(ICachingService cachingService, ILogger<CacheInvalidationService> logger)
        {
            _cachingService = cachingService;
            _logger = logger;
        }

        /// <summary>
        /// Invalidates cache entries for a specific entity
        /// </summary>
        /// <param name="entityType"> The entity type </param>
        /// <param name="entityId">   The entity ID </param>
        public void InvalidateEntityCache(string entityType, string entityId)
        {
            if (_entityCachePatterns.TryGetValue(entityType, out var patterns))
            {
                foreach (var pattern in patterns)
                {
                    var key = $"{pattern}{entityId}";
                    _cachingService.Remove(key);
                    _logger.LogInformation("Invalidated cache for {EntityType} with ID {EntityId}, key: {Key}", entityType, entityId, key);
                }
            }
        }

        /// <summary>
        /// Invalidates all cache entries for a specific entity type
        /// </summary>
        /// <param name="entityType"> The entity type </param>
        public void InvalidateEntityTypeCache(string entityType)
        {
            if (_entityCachePatterns.TryGetValue(entityType, out var patterns))
            {
                foreach (var pattern in patterns)
                {
                    // We can't easily remove all keys with a specific prefix in MemoryCache, so
                    // we'll remove the collection cache keys that we know about
                    var collectionKey = $"{pattern}all";
                    _cachingService.Remove(collectionKey);

                    var pagedKey = $"{pattern}paged";
                    _cachingService.Remove(pagedKey);

                    _logger.LogInformation("Invalidated collection cache for {EntityType}, keys: {CollectionKey}, {PagedKey}",
                        entityType, collectionKey, pagedKey);
                }
            }
        }
    }

    /// <summary>
    /// Interface for cache invalidation service
    /// </summary>
    public interface ICacheInvalidationService
    {
        /// <summary>
        /// Invalidates cache entries for a specific entity
        /// </summary>
        /// <param name="entityType"> The entity type </param>
        /// <param name="entityId">   The entity ID </param>
        void InvalidateEntityCache(string entityType, string entityId);

        /// <summary>
        /// Invalidates all cache entries for a specific entity type
        /// </summary>
        /// <param name="entityType"> The entity type </param>
        void InvalidateEntityTypeCache(string entityType);
    }
}