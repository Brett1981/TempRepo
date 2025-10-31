namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Extension methods for global exception handling
    /// </summary>
    public static class GlobalExceptionHandlerExtensions
    {
        /// <summary>
        /// Adds global exception handling to the request pipeline
        /// </summary>
        /// <param name="builder"> The application builder </param>
        /// <returns> The application builder </returns>
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}