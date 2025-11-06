using System;
using System.Threading;

namespace Sage200Microservice.Services.Infrastructure
{
    /// <summary>
    /// Ambient per-async-flow context to carry routing headers (Site/Company/ApiKey)
    /// for background operations (e.g., Kafka consumers, scheduled jobs) with no HttpContext.
    /// </summary>
    public sealed class SageCallContext
    {
        private static readonly AsyncLocal<SageCallContext?> _current = new();

        /// <summary>Current ambient context for the execution flow.</summary>
        public static SageCallContext? Current => _current.Value;

        /// <summary>Optional site id for Sage calls.</summary>
        public string? SiteId { get; init; }

        /// <summary>Optional company id for Sage calls.</summary>
        public string? CompanyId { get; init; }

        /// <summary>Optional API key identifying the calling application.</summary>
        public string? ApiKey { get; init; }

        private SageCallContext() { }

        /// <summary>
        /// Pushes a temporary context onto the ambient AsyncLocal for the current async flow.
        /// Dispose the returned scope to restore the previous context.
        /// </summary>
        public static IDisposable Push(string? siteId, string? companyId, string? apiKey)
        {
            var prior = _current.Value;
            _current.Value = new SageCallContext
            {
                SiteId = siteId,
                CompanyId = companyId,
                ApiKey = apiKey
            };
            return new RestoreScope(prior);
        }

        private sealed class RestoreScope : IDisposable
        {
            private readonly SageCallContext? _prior;
            private bool _disposed;
            public RestoreScope(SageCallContext? prior) => _prior = prior;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current.Value = _prior;
            }
        }
    }
}
