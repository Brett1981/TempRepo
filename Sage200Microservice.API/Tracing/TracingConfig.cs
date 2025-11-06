using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sage200Microservice.Services.Shared;
using System.Text.RegularExpressions;

namespace Sage200Microservice.API.Tracing
{
    /// <summary>
    /// Configuration for distributed tracing
    /// </summary>
    public static class TracingConfig
    {
        /// <summary>
        /// Adds OpenTelemetry tracing to the service collection
        /// </summary>
        public static IServiceCollection AddDistributedTracing(this IServiceCollection services, IConfiguration configuration)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName: "Sage200Microservice.API", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = configuration["Environment"] ?? "development",
                    ["service.instance.id"] = Environment.MachineName
                });

            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder.SetResourceBuilder(resourceBuilder);

                    // ASP.NET Core
                    builder.AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.request.headers.user_agent", request.Headers["User-Agent"].ToString());
                            activity.SetTag("http.request.headers.host", request.Headers["Host"].ToString());
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            activity.SetTag("http.response.headers.content_type", response.Headers["Content-Type"].ToString());
                        };
                        // Reduce noise
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    });

                    // HttpClient
                    builder.AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (request.RequestUri != null)
                            {
                                activity.SetTag("http.url", request.RequestUri.ToString());
                                activity.SetTag("http.host", request.RequestUri.Host);
                            }
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            var ct = response?.Content?.Headers?.ContentType?.ToString();
                            if (!string.IsNullOrEmpty(ct))
                                activity.SetTag("http.response.content_type", ct);
                        };
                    });

                    // SQL Client
                    builder.AddSqlClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                        options.EnableConnectionLevelAttributes = true;

                        // Sanitize the db.statement tag
                        options.Enrich = (activity, eventName, rawObject) =>
                        {
                            if (eventName == "OnCommandStart" && rawObject is System.Data.Common.DbCommand cmd)
                            {
                                var sanitized = OData.SanitizeSql(cmd.CommandText);
                                activity.SetTag("db.statement", sanitized);
                            }
                        };
                    });

                    // EF Core
                    builder.AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                    });

                    builder.AddSource("Sage200Microservice.API");
                    builder.AddSource("Sage200Microservice.Services");
                    builder.AddSource("Sage200Microservice.Data");

                    builder.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.25)));

                    var exporterType = configuration["OpenTelemetry:Exporter"] ?? "console";
                    switch (exporterType.ToLower())
                    {
                        case "jaeger":
                            builder.AddJaegerExporter(o =>
                            {
                                o.AgentHost = configuration["OpenTelemetry:Jaeger:Host"] ?? "localhost";
                                o.AgentPort = int.Parse(configuration["OpenTelemetry:Jaeger:Port"] ?? "6831");
                                o.MaxPayloadSizeInBytes = 4096;
                                o.ExportProcessorType = ExportProcessorType.Batch;
                            });
                            break;

                        case "otlp":
                            builder.AddOtlpExporter(o =>
                            {
                                o.Endpoint = new Uri(configuration["OpenTelemetry:Otlp:Endpoint"] ?? "http://localhost:4317");
                                o.Protocol = OtlpExportProtocol.Grpc;
                            });
                            break;

                        case "zipkin":
                            builder.AddZipkinExporter(o =>
                            {
                                o.Endpoint = new Uri(configuration["OpenTelemetry:Zipkin:Endpoint"] ?? "http://localhost:9411/api/v2/spans");
                            });
                            break;

                        case "console":
                        default:
                            builder.AddConsoleExporter();
                            break;
                    }
                })
                .WithMetrics(builder =>
                {
                    builder.SetResourceBuilder(resourceBuilder);

                    builder.AddAspNetCoreInstrumentation();
                    builder.AddHttpClientInstrumentation();
                    builder.AddRuntimeInstrumentation();
                    builder.AddProcessInstrumentation();

                    var exporterType = configuration["OpenTelemetry:MetricsExporter"] ?? "console";
                    switch (exporterType.ToLower())
                    {
                        case "otlp":
                            builder.AddOtlpExporter(o =>
                            {
                                o.Endpoint = new Uri(configuration["OpenTelemetry:Otlp:Endpoint"] ?? "http://localhost:4317");
                                o.Protocol = OtlpExportProtocol.Grpc;
                            });
                            break;

                        case "prometheus":
                            builder.AddPrometheusExporter();
                            break;

                        case "console":
                        default:
                            builder.AddConsoleExporter();
                            break;
                    }
                });

            return services;
        }

        /// <summary>
        /// Configures OpenTelemetry logging
        /// </summary>
        public static ILoggingBuilder AddOpenTelemetryLogging(this ILoggingBuilder builder, IConfiguration configuration)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName: "Sage200Microservice.API", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = configuration["Environment"] ?? "development",
                    ["service.instance.id"] = Environment.MachineName
                });

            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);

                var exporterType = configuration["OpenTelemetry:LogExporter"] ?? "console";
                switch (exporterType.ToLower())
                {
                    case "otlp":
                        options.AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri(configuration["OpenTelemetry:Otlp:Endpoint"] ?? "http://localhost:4317");
                            otlp.Protocol = OtlpExportProtocol.Grpc;
                        });
                        break;

                    case "console":
                    default:
                        options.AddConsoleExporter();
                        break;
                }
            });

            return builder;
        }

        /// <summary>
        /// Placeholder for extra tracing-related middleware
        /// </summary>
        public static IApplicationBuilder UseDistributedTracing(this IApplicationBuilder app) => app;

    }
}