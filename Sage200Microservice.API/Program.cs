using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc; // ApiExplorerSettingsAttribute
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sage200Microservice.API;
using Sage200Microservice.API.Configuration;
using Sage200Microservice.API.Extensions;
using Sage200Microservice.API.Grpc;
using Sage200Microservice.API.HealthChecks;
using Sage200Microservice.API.Logging;
using Sage200Microservice.API.Metrics;
using Sage200Microservice.API.Middleware;
using Sage200Microservice.API.Monitoring;
using Sage200Microservice.API.Services;
using Sage200Microservice.API.Tracing;
using Sage200Microservice.API.Validators;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Http;
using Sage200Microservice.Services.Implementations;
using Sage200Microservice.Services.Implementations.Sales;
using Sage200Microservice.Services.Implementations.Sop;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Logging;
using Sage200Microservice.Services.Logging.Encryption;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Payments;
using Sage200Microservice.Services.Security;
using Serilog;
using System.Net.Mime;
using System.Text.Json.Serialization;

Console.WriteLine("BOOT: starting main");

// --------------------------------------------------------------------------- Global
// crash/observability hooks (unchanged behaviour) ---------------------------------------------------------------------------
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.Error.WriteLine($"UNHANDLED EXCEPTION: {e.ExceptionObject}");
    if (e.ExceptionObject is Exception ex) Log.Fatal(ex, "UNHANDLED EXCEPTION");
};
TaskScheduler.UnobservedTaskException += (s, e) =>
{
    Console.Error.WriteLine($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
    Log.Error(e.Exception, "UNOBSERVED TASK EXCEPTION");
    e.SetObserved();
};

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 1) Host/Logging
    builder.Host.UseSerilogLogging();

    // 2) Kestrel endpoints
    ProgramBootstrap.ConfigureKestrel(builder);

    // 3) OpenTelemetry + DataProtection + Distributed Tracing
    ProgramBootstrap.ConfigureTracingAndProtection(builder);

    // 4) MVC + JSON + FluentValidation + ProblemDetails
    ProgramBootstrap.ConfigureControllersAndValidation(builder);

    // 5) EF Core
    ProgramBootstrap.ConfigureDbContext(builder);

    // 6) DI: repositories & domain services (includes Sales/SOP)
    ProgramBootstrap.RegisterRepositories(builder);
    ProgramBootstrap.RegisterDomainServices(builder);

    // 7) Options / feature config / encryption
    ProgramBootstrap.ConfigureOptionsAndEncryption(builder);

    // 8) HTTP clients (Sage auth + API client) + HttpContextAccessor
    ProgramBootstrap.ConfigureHttpClients(builder);

    // 9) Caching + Hosted services (background jobs)
    ProgramBootstrap.ConfigureCachingAndHostedServices(builder);

    // 10) AuthZ policy & (non-interactive) Authentication placeholder
    ProgramBootstrap.ConfigureAuth(builder);

    // 11) Observability, Swagger, rate-limiting, CORS, headers
    ProgramBootstrap.ConfigureCrossCutting(builder);

    // 12) gRPC + JSON transcoding + Swagger
    ProgramBootstrap.ConfigureGrpc(builder);

    // 13) Kafka publisher (feature-flagged)
    ProgramBootstrap.ConfigureEventPublisher(builder);
    builder.Services.AddKafkaConsumers(builder.Configuration);

    // Register Kafka consumer liveness health check only when Kafka is enabled
    if (builder.Configuration.GetValue<bool>("Features:Kafka:Enabled"))
    {
        builder.Services
            .AddHealthChecks()
            .AddCheck<KafkaConsumerLivenessHealthCheck>("kafka_consumer_liveness");
    }
    // Build
    var app = builder.Build();

    // Dev - only inbound API key fallback BEFORE your API key enforcement middleware/ filters
    if (app.Environment.IsDevelopment())
    {
        app.UseMiddleware<DevApiKeyFallbackMiddleware>();
    }

    // 15) Pipeline (exception handler, dev extras, headers, tracing, rate-limit, etc.)
    ProgramBootstrap.ConfigurePipeline(app);

    // 16) Endpoint maps (controllers, gRPC, dashboard)
    ProgramBootstrap.MapEndpoints(app);

    // 17) Health check UI
    app.UseHealthChecksConfig();
    app.UseHealthCheckDashboard(app.Configuration);

    // 18) Start & wait for shutdown (preserve original lifecycle)
    Log.Information("Starting web host...");
    await app.StartAsync();
    Log.Information("Now listening on: {Urls}", string.Join(", ", app.Urls));
    await app.WaitForShutdownAsync();
    Log.Information("Host shutdown complete");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: " + ex);
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ============================================================================ Helpers + Bootstrap
// (kept in-file for clarity; no functional changes) ============================================================================

/// <summary>
/// Groups bootstrapping operations for readability while preserving original behaviour.
/// </summary>
internal static class ProgramBootstrap
{
    /// <summary>
    /// Configures Kestrel listeners from configuration:
    /// - Hosting:AuthUrl (HTTPS with PFX from Certificates:Auth)
    /// - Hosting:LocalHttpsUrl (dev HTTPS)
    /// - Hosting:EnableLocalHttp + Hosting:LocalHttpUrl (dev HTTP)
    /// </summary>
    public static void ConfigureKestrel(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel((ctx, opt) =>
        {
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("KestrelAuth");
            var envSvc = ctx.HostingEnvironment;

            var authUrl = ctx.Configuration["Hosting:AuthUrl"];
            if (!string.IsNullOrWhiteSpace(authUrl))
            {
                var u = new Uri(authUrl);
                var (pfx, pwd) = ResolveAuthPfx(ctx, logger, envSvc);
                opt.Listen(System.Net.IPAddress.Any, u.Port, lo => lo.UseHttps(pfx, pwd));
            }

            var localHttps = ctx.Configuration["Hosting:LocalHttpsUrl"];
            if (!string.IsNullOrWhiteSpace(localHttps))
            {
                var u = new Uri(localHttps);
                opt.ListenLocalhost(u.Port, lo => lo.UseHttps());
            }

            var enableHttp = ctx.Configuration.GetValue("Hosting:EnableLocalHttp", false);
            var localHttp = ctx.Configuration["Hosting:LocalHttpUrl"];
            if (enableHttp && !string.IsNullOrWhiteSpace(localHttp))
            {
                var u = new Uri(localHttp);
                opt.ListenLocalhost(u.Port);
            }
        });
    }

    /// <summary> Adds OpenTelemetry logging & tracing plus DataProtection persisted to ./keys. </summary>
    public static void ConfigureTracingAndProtection(WebApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetryLogging(builder.Configuration);

        builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "keys")))
            .SetApplicationName("Sage200Microservice");

        builder.Services.AddDistributedTracing(builder.Configuration);
    }

    /// <summary>
    /// MVC with JSON options, ValidationFilter, FluentValidation discovery and custom 400 ProblemDetails.
    /// </summary>
    public static void ConfigureControllersAndValidation(WebApplicationBuilder builder)
    {
        builder.Services
            .AddControllers(o => o.Filters.Add<Sage200Microservice.API.Filters.ValidationFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                o.JsonSerializerOptions.PropertyNamingPolicy = null;
                o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString; // e.g. "00000162"
            });
        builder.Services.AddOptions();
        // Validators (assemblies preserved)
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<CreateCustomerRequestValidator>();
        builder.Services.AddValidatorsFromAssemblyContaining<SopOrderStatusUpdateValidator>();
        builder.Services.AddValidatorsFromAssemblyContaining<SopOrderCreateValidator>();

        // ModelState → ProblemDetails(400)
        builder.Services.Configure<ApiBehaviorOptions>(opt =>
        {
            opt.InvalidModelStateResponseFactory = ctx =>
            {
                var problem = new ValidationProblemDetails(ctx.ModelState)
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Invalid request payload",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more JSON fields are invalid. See 'errors' for details."
                };
                return new BadRequestObjectResult(problem);
            };
        });
    }

    /// <summary>
    /// Registers ApplicationContext with resilient SQL Server settings.
    /// </summary>
    public static void ConfigureDbContext(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<ApplicationContext>((sp, options) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

            // --- ADD THIS LOGGING ---
            // Get a logger instance from the service provider
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbContextConfig");
            // Log whether the connection string was found
            logger.LogInformation("ConfigureDbContext: ConnectionString 'DefaultConnection' IsNullOrEmpty = {IsNullOrWhiteSpace}", string.IsNullOrEmpty(cs));
            // --- END ADD LOGGING ---

            // Defensive check: Throw explicitly if connection string is missing during configuration
            if (string.IsNullOrEmpty(cs))
            {
                logger.LogError("FATAL: ConnectionString 'DefaultConnection' is missing or empty in configuration. Cannot configure DbContext.");
                throw new InvalidOperationException("ConnectionString 'DefaultConnection' is missing or empty in configuration.");
            }


            options.UseSqlServer(cs, sql =>
            {
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                sql.CommandTimeout(60);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                sql.MaxBatchSize(100);
                // Ensure migrations assembly is specified if it wasn't implicitly found
                sql.MigrationsAssembly("Sage200Microservice.Data");
            });
        });
    }

    /// <summary>
    /// Repository registrations (scoped).
    /// </summary>
    public static void RegisterRepositories(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
        builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        builder.Services.AddScoped<IInvoiceStatusHistoryRepository, InvoiceStatusHistoryRepository>();
        builder.Services.AddScoped<IApiLogRepository, ApiLogRepository>();
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        builder.Services.AddScoped<IExternalIdLinkRepository, ExternalIdLinkRepository>();
        builder.Services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
    }

    /// <summary>
    /// Domain services registrations (scoped).
    /// </summary>
    public static void RegisterDomainServices(WebApplicationBuilder builder)
    {
        // Status & SOP
        builder.Services.AddScoped<ISopOrderStatusService, SopOrderStatusService>();
        builder.Services.AddScoped<ISopDocumentStatusTypeService, SopDocumentStatusTypeService>();
        builder.Services.AddScoped<ISopOrderService, SopOrderService>();

        // Auth / Sage API
        builder.Services.AddScoped<ISageAuthenticationService, SageAuthenticationService>();
        builder.Services.AddScoped<ISageApiClient, SageApiClient>();
        builder.Services.AddScoped<IOAuthTokenStore, OAuthTokenStore>();

        // Business services
        builder.Services.AddScoped<ICustomerService, CustomerService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddScoped<IBatchProcessingService, BatchProcessingService>();
        builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();

        // Sales (URN entities)
        builder.Services.AddScoped<ISalesReceiptsService, SalesReceiptsService>();
        builder.Services.AddScoped<ISalesPaymentsService, SalesPaymentsService>();
        builder.Services.AddScoped<ISalesCreditNotesService, SalesCreditNotesService>();
        builder.Services.AddScoped<ISalesInvoicesService, SalesInvoicesService>();

        // Payments allocation
        builder.Services.AddScoped<IPaymentsAllocationService, PaymentsAllocationService>();

        // Sync and Reconciliation Services
        builder.Services.AddScoped<ISyncService, SyncService>();
        builder.Services.AddScoped<IReconciliationService, ReconciliationService>();
    }

    /// <summary>
    /// Binds options, performs post-config validations, logging writer/reader and field encryptor.
    /// </summary>
    public static void ConfigureOptionsAndEncryption(WebApplicationBuilder builder)
    {
        builder.Services.Configure<SageApiSettings>(builder.Configuration.GetSection("SageApi"));
        builder.Services.PostConfigure<SageApiSettings>(opt => opt.ValidateAndNormalize());
        builder.Services.Configure<ServiceApiKeyOptions>(o => { /* uses defaults */ });

        builder.Services.Configure<AuditLogSettings>(builder.Configuration.GetSection("AuditLogging"));
        builder.Services.Configure<BackgroundServiceSettings>(builder.Configuration.GetSection("BackgroundServices"));
        builder.Services.Configure<InvoiceStatusServiceSettings>(builder.Configuration.GetSection("BackgroundServices:InvoiceStatus"));
        builder.Services.Configure<ApiKeyRotationOptions>(builder.Configuration.GetSection("ApiKeyRotation"));
        builder.Services.Configure<TokenMaintenanceOptions>(builder.Configuration.GetSection("TokenMaintenance"));
        builder.Services.Configure<DbApiLoggingOptions>(builder.Configuration.GetSection("Logging:ApiLogs"));

        builder.Services.AddScoped<Sage200Microservice.Services.Logging.IDbLogWriter, Sage200Microservice.Services.Logging.DbLogWriter>();
        builder.Services.AddScoped<IDbLogReader, DbLogReader>();

        // AES-256-GCM field encryption for ApiLogs payloads
        builder.Services.Configure<AesGcmFieldEncryptor.Options>(builder.Configuration.GetSection("Logging:ApiLogs"));
        builder.Services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
    }

    /// <summary>
    /// Configures named HttpClients (SageAuth, ISageApiClient) + HttpContextAccessor.
    /// Includes SageRoutingHeaderHandler for automatic header injection (X-Site, X-Company, X-Api-Key).
    /// </summary>
    public static void ConfigureHttpClients(WebApplicationBuilder builder)
    {
        // === 1. Register individual handlers in DI ===
        builder.Services.AddTransient<Sage200Microservice.Services.Http.CorrelationIdHandler>();
        builder.Services.AddTransient<Sage200Microservice.Services.Http.SageApiLoggingHandler>();
        builder.Services.AddTransient<SageAuthDelegatingHandler>();
        builder.Services.AddTransient<SageRoutingHeaderHandler>(); // <-- NEW: the automatic header injector
        builder.Services.AddHttpContextAccessor();

        // === 2. Register the SageAuth helper client ===
        builder.Services.AddHttpClient("SageAuth", c =>
        {
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 100,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
        });

        // === 3. Register the main typed Sage API client ===
        builder.Services.AddHttpClient<ISageApiClient, SageApiClient>((sp, http) =>
        {
            var cfg = sp.GetRequiredService<IOptions<SageApiSettings>>().Value;
            http.BaseAddress = new Uri(
                cfg.BaseUrl.EndsWith("/") ? cfg.BaseUrl : cfg.BaseUrl + "/",
                UriKind.Absolute);
        })
        // Handler order = first added executes **last** (inside-out).
        // So the request pipeline will run:
        //   HttpClient → SageAuthDelegatingHandler → SageRoutingHeaderHandler → SageApiLoggingHandler → CorrelationIdHandler
        // The routing handler sits before auth, ensuring headers exist before tokens are attached.
        .AddHttpMessageHandler<Sage200Microservice.Services.Http.CorrelationIdHandler>()
        .AddHttpMessageHandler<Sage200Microservice.Services.Http.SageApiLoggingHandler>()
        .AddHttpMessageHandler<SageRoutingHeaderHandler>()
        .AddHttpMessageHandler<SageAuthDelegatingHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 100,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
        });
    }


    /// <summary>
    /// Adds memory/distributed cache and hosted services (InvoiceStatus, ApiKeyRotation,
    /// TokenMaintenance, AuditLogCleanup).
    /// </summary>
    public static void ConfigureCachingAndHostedServices(WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ICachingService, CachingService>();
        builder.Services.AddSingleton<ICacheInvalidationService, CacheInvalidationService>();
        builder.Services.AddHostedService<InvoiceStatusBackgroundService>();
        builder.Services.AddHostedService<ApiKeyRotationService>();
        builder.Services.AddHostedService<TokenMaintenanceService>();
        builder.Services.AddHostedService<AuditLogCleanupService>();

        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
    }

    /// <summary>
    /// Adds Authentication placeholder and "ApiUser" Authorization policy (Dev-allow; Non-Dev
    /// require API key or authenticated user).
    /// </summary>
    public static void ConfigureAuth(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(); // no interactive schemes; API key is middleware
        var isDev = builder.Environment.IsDevelopment();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ApiUser", policy =>
                policy.RequireAssertion(ctx =>
                {
                    if (isDev) return true;
                    var http = ctx.Resource as HttpContext;
                    if (http != null && http.Request.Headers.ContainsKey("X-Api-Key")) return true;
                    return ctx.User?.Identity?.IsAuthenticated == true;
                }));
        });
    }

    /// <summary>
    /// Health checks, dashboards, error monitoring, metrics, Swagger, Serilog services, rate limit,
    /// CORS, security headers, compression.
    /// </summary>
    public static void ConfigureCrossCutting(WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecksConfig(builder.Configuration);
        builder.Services.AddHealthCheckDashboard(builder.Configuration);
        builder.Services.AddErrorMonitoring(builder.Configuration);
        builder.Services.AddBusinessMetrics(builder.Configuration);
        builder.Services.AddSingleton<Sage200Microservice.API.Metrics.BackgroundServiceMetrics>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerDocumentation();
        builder.Services.AddRateLimiting(builder.Configuration);
        builder.Services.AddIpFiltering(builder.Configuration);
        builder.Services.AddSerilogServices();

        // Rate limit keyed by API key contributor
        builder.Services.AddSingleton<Sage200Microservice.API.Configuration.ApiKeyClientResolveContributor>();
        builder.Services.AddSingleton<AspNetCoreRateLimit.IClientResolveContributor>(sp =>
            sp.GetRequiredService<Sage200Microservice.API.Configuration.ApiKeyClientResolveContributor>());

        builder.Services.AddRateLimiting(builder.Configuration);
        builder.Services.AddScoped<Sage200Microservice.API.Middleware.IpFilteringMiddleware>();
        builder.Services.AddCorsPolicy(builder.Configuration);
        builder.Services.AddSecurityHeaders(builder.Configuration);
        builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
    }

    /// <summary>
    /// gRPC with JSON transcoding, reflection (dev) and Swagger descriptors.
    /// </summary>
    public static void ConfigureGrpc(WebApplicationBuilder builder)
    {
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcSageHeaderInterceptor>();
        }).AddJsonTranscoding();
        builder.Services.AddGrpcReflection();
        builder.Services.AddGrpcSwagger();
    }


    /// <summary>
    /// Configures Kafka publisher behind feature flags; falls back to NoOp publisher.
    /// </summary>
    public static void ConfigureEventPublisher(WebApplicationBuilder builder)
    {
        builder.Services.AddKafkaPublisher(builder.Configuration);

        //var kafkaEnabled = builder.Configuration.GetValue("Features:Kafka:Enabled", false);
        //var sopPublishEnabled = builder.Configuration.GetValue("Features:Sop:PublishCreatedEventEnabled", false);

        //if (kafkaEnabled && sopPublishEnabled)
        //{
        //    builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        //}
        //else
        //{
        //    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
        //}
    }

    /// <summary>
    /// Builds the HTTP pipeline with the same middleware order and behaviour as before. Includes
    /// dev-only Swagger UI and diagnostic logs, global exception → RFC7807, tracing, security
    /// headers, cors, rate limiting, API key gate (non-dev), authN/Z, logging, audit, Swagger docs.
    /// </summary>
    public static void ConfigurePipeline(WebApplication app)
    {
        // RFC7807 for unhandled exceptions (terminal)
        app.UseExceptionHandler(eApp =>
        {
            eApp.Run(async ctx =>
            {
                var ex = ctx.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                var problem = new
                {
                    type = "about:blank",
                    title = "An unexpected error occurred.",
                    status = 500,
                    detail = ex?.Message,
                    traceId = ctx.TraceIdentifier
                };
                ctx.Response.ContentType = MediaTypeNames.Application.Json;
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(problem);
            });
        });

        if (app.Environment.IsDevelopment())
        {
            var diagLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EndpointDiag");
            app.LogAuthLoginDuplicates(diagLogger);
            app.LogDuplicateRoutes(diagLogger);

            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sage200Microservice API v1");
                c.RoutePrefix = "swagger";
            });

            app.MapGrpcReflectionService();
        }

        // OAuth config diagnostic
        OAuthStartupDiag.LogOAuthConfig(app.Configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthDiag"));

        // Core pipeline (order preserved)
        app.UseGlobalExceptionHandler();
        app.UseTracing();
        app.UseDistributedTracing();
        app.UseResponseCompression();
        app.UseHttpsRedirection();
        app.UseIpFiltering();
        app.UseStaticFiles();
        app.UseSecurityHeaders();
        app.UseMiddleware<Sage200Microservice.API.Middleware.IpFilteringMiddleware>();
        app.UseCorsPolicy(app.Configuration);
        app.UseRateLimiting();

        if (!app.Environment.IsDevelopment())
        {
            // Require API key for everything EXCEPT auth, swagger, health, and the dashboard (html/js) & its data endpoints
            app.UseWhen(ctx =>
                !ctx.Request.Path.StartsWithSegments("/auth") &&
                !ctx.Request.Path.StartsWithSegments("/swagger") &&
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/business-dashboard") &&
                !ctx.Request.Path.StartsWithSegments("/business-dashboard.html") &&
                !ctx.Request.Path.StartsWithSegments("/dashboardscript.js") &&
                !ctx.Request.Path.StartsWithSegments("/api/business-metrics") &&
                !ctx.Request.Path.StartsWithSegments("/api/businessmetrics") &&
                !ctx.Request.Path.StartsWithSegments("/metrics"),
                branch => branch.UseApiKeyAuthentication());
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSerilogRequestLogging();
        app.UseAuditLogging();
        app.UseRateLimiting();
        app.UseSwaggerDocumentation();
    }

    /// <summary>
    /// Maps controllers, gRPC services (hidden from ApiExplorer) and /business-dashboard redirect.
    /// </summary>
    public static void MapEndpoints(WebApplication app)
    {
        app.MapControllers();

        app.MapGrpcService<InvoiceGrpcService>()
            .WithMetadata(new ApiExplorerSettingsAttribute { IgnoreApi = true });

        app.MapGrpcService<SopGrpcService>()
            .WithMetadata(new ApiExplorerSettingsAttribute { IgnoreApi = true });

        app.MapGet("/business-dashboard", ctx =>
        {
            ctx.Response.Redirect("/business-dashboard.html");
            return Task.CompletedTask;
        });
    }

    // ---------- Private helper methods (moved from top for clarity) ----------

    /// <summary>
    /// Expands env-vars and ~/ paths relative to content root; returns rooted absolute path.
    /// </summary>
    private static string ExpandPath(string path, IWebHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
            expanded = Path.Combine(env.ContentRootPath, expanded[2..]);
        if (!Path.IsPathRooted(expanded))
            expanded = Path.Combine(env.ContentRootPath, expanded);
        return expanded;
    }

    /// <summary>
    /// Resolves the Auth TLS PFX and logs the chosen file.
    /// </summary>
    private static (string PfxPath, string Password) ResolveAuthPfx(WebHostBuilderContext ctx, Microsoft.Extensions.Logging.ILogger log, IWebHostEnvironment env)
    {
        var rawPath = ctx.Configuration["Certificates:Auth:PfxPath"] ?? "";
        var pwd = ctx.Configuration["Certificates:Auth:Password"] ?? "";
        var path = ExpandPath(rawPath, env);

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Certificates:Auth:PfxPath is missing.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Auth PFX not found at '{path}'. Ensure the file exists and the process has read permission.");

        log.LogInformation("Using Auth PFX at '{Path}' for HTTPS endpoint.", path);
        return (path, pwd);
    }

}