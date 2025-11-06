using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Configuration;
using Sage200Microservice.Services.Infrastructure;
using Sage200Microservice.Services.Models;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Grpc
{
    /// <summary>
    /// gRPC server-side interceptor to populate SageCallContext from incoming metadata,
    /// so background Sage calls triggered by gRPC handlers inherit Site/Company/ApiKey.
    /// Applies Dev API key fallback if permitted.
    /// </summary>
    public sealed class GrpcSageHeaderInterceptor : Interceptor
    {
        private readonly SageApiSettings _settings;
        private readonly IHostEnvironment _env;

        public GrpcSageHeaderInterceptor(IOptions<SageApiSettings> settings, IHostEnvironment env)
        {
            _settings = settings.Value;
            _env = env;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            string? site = context.RequestHeaders.GetValue(_settings.SiteHeaderName);
            string? company = context.RequestHeaders.GetValue(_settings.CompanyHeaderName);
            string? apiKey = context.RequestHeaders.GetValue(_settings.ApiKeyHeaderName);

            if (_env.IsDevelopment()
                && string.IsNullOrWhiteSpace(apiKey)
                && _settings.AllowDevelopmentFallbackApiKey
                && !string.IsNullOrWhiteSpace(_settings.DevelopmentDefaultApiKey))
            {
                apiKey = _settings.DevelopmentDefaultApiKey;
            }

            using var _ = SageCallContext.Push(site, company, apiKey);
            return await base.UnaryServerHandler(request, context, continuation);
        }
    }

    internal static class MetadataExtensions
    {
        public static string? GetValue(this Metadata headers, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var entry in headers)
            {
                if (string.Equals(entry.Key, key, System.StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
            return null;
        }
    }
}
