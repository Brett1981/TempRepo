using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.Models;
using System.Net;
using System.Text.Json;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Middleware for handling exceptions globally (RFC7807).
    /// - Upstream 5xx/timeout that escape controllers are mapped to 424 Failed Dependency.
    /// - Do not interfere with controller-level degrade (they won’t throw).
    /// </summary>
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Add correlation ID to the context
                if (!context.Items.ContainsKey("CorrelationId"))
                {
                    context.Items["CorrelationId"] = Guid.NewGuid().ToString();
                }

                // Add correlation ID to the response headers
                context.Response.Headers.Append("X-Correlation-ID", context.Items["CorrelationId"].ToString());

                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsProblemDetailsAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsProblemDetailsAsync(HttpContext context, Exception exception)
        {
            // If the response has already started (e.g., a controller degraded to 200),
            // do not attempt to modify it.
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response already started; skipping global error handling.");
                return;
            }

            var correlationId = context.Items.ContainsKey("CorrelationId")
                ? context.Items["CorrelationId"].ToString()
                : Guid.NewGuid().ToString();

            // Log the exception with correlation ID
            using (_logger.BeginScope(new
            {
                CorrelationId = correlationId,
                RequestPath = context.Request.Path,
                RequestMethod = context.Request.Method
            }))
            {
                _logger.LogError(exception, "An unhandled exception occurred");
            }

            // Set the response status code and content type
            context.Response.StatusCode = MapStatusAndTitle(exception).status;
            context.Response.ContentType = "application/json";

            // Create the error response (keeps existing shape, includes correlation id)
            var errorResponse = new ErrorResponse
            {
                StatusCode = context.Response.StatusCode,
                Message = GetErrorMessage(exception),
                CorrelationId = correlationId
            };

            // Serialize the error response to JSON
            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Write the error response to the response body
            await context.Response.WriteAsync(json);
        }

        private static (int status, string title) MapStatusAndTitle(Exception exception)
        {
            // Upstream flakiness/timeout that bubbled → 424 Failed Dependency
            if (exception is TaskCanceledException)
                return ((int)HttpStatusCode.FailedDependency, "Upstream timeout");

            if (exception is HttpRequestException hre)
            {
                if (hre.StatusCode is null || (int)hre.StatusCode >= 500)
                    return ((int)HttpStatusCode.FailedDependency, "Upstream dependency failure");
                // 4xx from upstream: map to 400 unless controller chose otherwise
                if ((int)hre.StatusCode >= 400 && (int)hre.StatusCode < 500)
                    return ((int)HttpStatusCode.BadRequest, "Bad upstream request");
            }

            return ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred");
        }

        private static string GetErrorMessage(Exception exception)
        {
            return exception switch
            {
                ValidationException validationException => validationException.Message,
                ResourceNotFoundException notFoundException => notFoundException.Message,
                _ => "An unexpected error occurred. Please try again later."
            };
        }
    }
}