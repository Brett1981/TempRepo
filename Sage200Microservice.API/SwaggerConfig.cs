// Purpose: Configure Swagger to stop globally requiring an API key and to avoid injecting custom
// headers into every operation. We keep the ApiKey "definition" so devs can still click "Authorize"
// and provide a key manually in Swagger when testing, but we DO NOT add a global "requirement".
// The custom HeaderRequirementsOperationFilter is now a no-op (see its file).

using Microsoft.OpenApi.Models;
using Sage200Microservice.API.Swagger;
using System.Reflection;

namespace Sage200Microservice.API
{
    public static class SwaggerConfig
    {
        public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(c =>
            {
                // Basic doc
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Sage 200 API Microservice",
                    Version = "v1",
                    Description = "A microservice for integrating with the Sage 200 API",
                    Contact = new OpenApiContact
                    {
                        Name = "Stephen Brett",
                        Email = "stephen.brett@socotec.co.uk",
                        Url = new Uri("https://socotecuk.atlassian.net/servicedesk/customer/portals")
                    }
                });

                // Avoid schema collisions (nested/request DTOs)
                c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
                c.SupportNonNullableReferenceTypes();

                // Keep existing filters; HeaderRequirementsOperationFilter is now a no-op by design
                c.OperationFilter<Swagger.EnsureRequestBodyOperationFilter>();
                c.OperationFilter<HeaderRequirementsOperationFilter>();

                // Keep a (non-required) ApiKey security "definition" so devs can opt-in via the Authorize button.
                // IMPORTANT: Do NOT add a global SecurityRequirement, otherwise Swagger will treat it as mandatory.
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Description = "Optional API key for testing (header: X-Api-Key). Not required in Swagger.",
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Type = SecuritySchemeType.ApiKey
                });

                // Intentionally omitted:
                // c.AddSecurityRequirement(new OpenApiSecurityRequirement { ... });
                // This prevents Swagger from requiring ApiKey on every endpoint.

                // XML comments (if present)
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }
            });

            return services;
        }

        public static IApplicationBuilder UseSwaggerDocumentation(this IApplicationBuilder app)
        {
            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sage 200 API Microservice v1");
                c.RoutePrefix = string.Empty; // Swagger UI at root
                c.DocumentTitle = "Sage 200 API Documentation";
                c.DefaultModelsExpandDepth(-1); // Hide the big schemas sidebar by default

                // Keep your custom UI script if needed
                c.InjectJavascript("/swagger-ui/custom.js");
            });

            return app;
        }
    }
}
