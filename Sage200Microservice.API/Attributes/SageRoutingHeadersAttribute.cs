using System;

namespace Sage200Microservice.API.Attributes
{
    /// <summary>
    /// Marks controller actions that must expose Sage routing headers in Swagger.
    /// Set RequiresIdempotencyKey=true for create/duplicate endpoints.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SageRoutingHeadersAttribute : Attribute
    {
        public bool RequiresIdempotencyKey { get; set; } = false;
        /// <summary>Document X-Api-Key alongside routing headers (enforcement remains at middleware).</summary>
        public bool DocumentApiKey { get; init; } = true;
    }
}