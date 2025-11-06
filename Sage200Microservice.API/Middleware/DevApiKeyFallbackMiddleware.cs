using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Configuration;
using Sage200Microservice.Services.Models;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// In Development only, if the inbound HTTP request lacks X-Api-Key,
    /// injects the SageApi.DevelopmentDefaultApiKey into the request headers
    /// so downstream API key enforcement sees a valid key.
    /// </summary>
    public sealed class DevApiKeyFallbackMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHostEnvironment _env;
        private readonly SageApiSettings _settings;

        public DevApiKeyFallbackMiddleware(
            RequestDelegate next,
            IHostEnvironment env,
            IOptions<SageApiSettings> settings)
        {
            _next = next;
            _env = env;
            _settings = settings.Value;
        }

        public async Task Invoke(HttpContext context)
        {
            if (_env.IsDevelopment()
                && _settings.AllowDevelopmentFallbackApiKey
                && !string.IsNullOrWhiteSpace(_settings.DevelopmentDefaultApiKey)
                && !context.Request.Headers.ContainsKey(_settings.ApiKeyHeaderName))
            {
                context.Request.Headers[_settings.ApiKeyHeaderName] = _settings.DevelopmentDefaultApiKey!;
            }

            await _next(context);
        }
    }
}
