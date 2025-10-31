namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Extension methods for API key authentication
    /// </summary>
    public static class ApiKeyAuthenticationExtensions
    {
        /// <summary>
        /// Adds API key authentication to the request pipeline
        /// </summary>
        /// <param name="builder"> The application builder </param>
        /// <returns> The application builder </returns>
        public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
        }
    }
}