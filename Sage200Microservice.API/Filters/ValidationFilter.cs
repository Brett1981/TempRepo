using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sage200Microservice.API.Models;
using System.Net;

namespace Sage200Microservice.API.Filters
{
    /// <summary>
    /// Filter for validating requests using FluentValidation
    /// </summary>
    public class ValidationFilter : IAsyncActionFilter
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the ValidationFilter
        /// </summary>
        /// <param name="serviceProvider"> The service provider </param>
        public ValidationFilter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Executes the filter
        /// </summary>
        /// <param name="context"> The action executing context </param>
        /// <param name="next">    The action execution delegate </param>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Get all arguments from the action
            foreach (var argument in context.ActionArguments)
            {
                var argumentType = argument.Value?.GetType();
                if (argumentType == null)
                {
                    continue;
                }

                // Find a validator for this type
                var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
                if (_serviceProvider.GetService(validatorType) is not IValidator validator)
                {
                    continue;
                }

                // Validate the argument
                var validationContext = new ValidationContext<object>(argument.Value);
                var validationResult = await validator.ValidateAsync(validationContext);

                if (!validationResult.IsValid)
                {
                    // Create a dictionary of field errors
                    var errors = validationResult.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(e => e.ErrorMessage).ToArray()
                        );

                    // Create a validation exception with the errors
                    var exception = new FluentValidation.ValidationException(
                        "Validation failed. Please check the request and try again.",
                        validationResult.Errors);

                    // Create an error response
                    var errorResponse = new ErrorResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Message = "Validation failed. Please check the request and try again.",
                        CorrelationId = context.HttpContext.Items.ContainsKey("CorrelationId")
                            ? context.HttpContext.Items["CorrelationId"].ToString()
                            : Guid.NewGuid().ToString(),
                        Errors = errors
                    };

                    // Return a bad request with the error response
                    context.Result = new BadRequestObjectResult(errorResponse);
                    return;
                }
            }

            // If all validations pass, continue with the action
            await next();
        }
    }
}