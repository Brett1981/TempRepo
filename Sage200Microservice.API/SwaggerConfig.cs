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

                // Use fully-qualified names so nested request types don’t collide and requestBody schemas resolve correctly
                c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
                c.SupportNonNullableReferenceTypes();
                c.OperationFilter<Swagger.EnsureRequestBodyOperationFilter>();
                c.OperationFilter<HeaderRequirementsOperationFilter>();
                // Add API key authentication
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Description = "API key needed to access the endpoints. Example: &quot;ApiKey: my-api-key&quot;",
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Type = SecuritySchemeType.ApiKey
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "ApiKey"
                            }
                        },
                        Array.Empty<string>()
                    }
                });

                // Set the comments path for the Swagger JSON and UI
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
                c.DefaultModelsExpandDepth(-1);

                // Add this line to inject the custom JS
                c.InjectJavascript("/swagger-ui/custom.js");
            });

            return app;
        }
    }
}
