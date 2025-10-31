using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sage200Microservice.API.Swagger
{
    /// <summary>
    /// If ApiExplorer reports a body-bound parameter but the OpenAPI operation has
    /// no requestBody, add one with application/json. Fixes empty JSON editor in UI.
    /// </summary>
    public sealed class EnsureRequestBodyOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Is there a [FromBody] (or inferred) parameter?
            var bodyParam = context.ApiDescription.ParameterDescriptions
                .FirstOrDefault(p => p.Source == BindingSource.Body);
            if (bodyParam == null) return;

            // Already present? Nothing to do.
            if (operation.RequestBody != null) return;

            var schema = context.SchemaGenerator.GenerateSchema(
                bodyParam.Type, context.SchemaRepository);

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = bodyParam.IsRequired,
                Content =
                {
                    ["application/json"] = new OpenApiMediaType { Schema = schema }
                }
            };
        }
    }
}
