using Microsoft.OpenApi.Models;
using Sage200Microservice.API.Attributes;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;
using System.Reflection;

namespace Sage200Microservice.API.Swagger
{
    /// <summary>
    /// Injects X-Site, X-Company and (optionally) Idempotency-Key into Swagger UI for annotated actions.
    /// </summary>
    public sealed class HeaderRequirementsOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Try MethodInfo first, then ApiDescription, then EndpointMetadata (for minimal/health endpoints)
            SageRoutingHeadersAttribute? marker = null;

            // 1) Direct MethodInfo, when available
            var mi = context.MethodInfo;
            if (mi != null)
            {
                marker = mi.GetCustomAttributes(true)
                           .OfType<SageRoutingHeadersAttribute>()
                           .FirstOrDefault();
            }

            // 2) Fallback: ApiDescription can often provide MethodInfo
            if (marker == null && context.ApiDescription != null &&
                context.ApiDescription.TryGetMethodInfo(out MethodInfo? mi2) && mi2 != null)
            {
                marker = mi2.GetCustomAttributes(true)
                            .OfType<SageRoutingHeadersAttribute>()
                            .FirstOrDefault();
            }

            // 3) Final fallback: Endpoint metadata (covers minimal APIs/health endpoints)
            if (marker == null)
            {
                var endpointMeta = context.ApiDescription?.ActionDescriptor?.EndpointMetadata;
                if (endpointMeta != null)
                {
                    marker = endpointMeta.OfType<SageRoutingHeadersAttribute>().FirstOrDefault();
                }
            }

            if (marker == null)
            {
                // Not annotated → nothing to inject
                return;
            }

            operation.Parameters ??= new List<OpenApiParameter>();

            AddRequiredHeader(operation, "X-Site", "Site GUID for Sage routing");
            AddRequiredHeader(operation, "X-Company", "Company identifier for Sage routing");

            if (marker.RequiresIdempotencyKey)
            {
                AddOptionalHeader(operation, "Idempotency-Key", "Provide to ensure idempotent create/duplicate");
            }

            if (marker.DocumentApiKey)
            {
                AddRequiredHeader(operation, "X-Api-Key", "API key of the calling application (middleware-enforced).");
            }
        }

        private static void AddRequiredHeader(OpenApiOperation op, string name, string description)
        {
            if (op.Parameters.Any(p => p.Name == name && p.In == ParameterLocation.Header)) return;
            op.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Header,
                Required = true,
                Description = description,
                Schema = new OpenApiSchema { Type = "string" }
            });
        }

        private static void AddOptionalHeader(OpenApiOperation op, string name, string description)
        {
            if (op.Parameters.Any(p => p.Name == name && p.In == ParameterLocation.Header)) return;
            op.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Header,
                Required = false,
                Description = description,
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}