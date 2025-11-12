// Purpose: Previously injected X-Site, X-Company, Idempotency-Key, and X-Api-Key headers into Swagger
// for actions annotated with SageRoutingHeadersAttribute. Business decision: Swagger should NOT display
// or require those headers anywhere. Runtime enforcement/auto-generation still happens in middleware
// and controllers; this filter is now a deliberate no-op.

using Microsoft.OpenApi.Models;
using Sage200Microservice.API.Attributes;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sage200Microservice.API.Swagger
{
    /// <summary>
    /// (Disabled) Operation filter for routing/header documentation.
    /// <para>
    /// We intentionally do nothing so Swagger remains clean and does not require/display:
    /// - X-Site
    /// - X-Company
    /// - X-Api-Key
    /// - Idempotency-Key
    /// </para>
    /// <para>
    /// NOTE: Runtime behavior is unchanged. The pipeline and controllers still read and enforce these
    /// headers (or auto-generate idempotency keys) as per the business rules. This only affects Swagger UI.
    /// </para>
    /// </summary>
    public sealed class HeaderRequirementsOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Intentionally no-op.
            // If you ever want to re-enable specific headers in Swagger, add them here conditionally.
            // Example: add Idempotency-Key only for POST creates when a config flag is true.
        }
    }
}
