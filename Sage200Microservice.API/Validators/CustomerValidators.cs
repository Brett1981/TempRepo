// API/Validators/CustomerValidators.cs
using FluentValidation;
using Sage200Microservice.API.DTOs;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// Validator for customer creation requests
    /// </summary>
    public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequestDto>
    {
        public CreateCustomerRequestValidator()
        {
            RuleFor(x => x.CustomerName)
                .NotEmpty().WithMessage("Customer name is required")
                .MaximumLength(100).WithMessage("Customer name cannot exceed 100 characters");

            RuleFor(x => x.CustomerCode)
                .NotEmpty().WithMessage("Customer code is required")
                .MaximumLength(20).WithMessage("Customer code cannot exceed 20 characters")
                .Matches(@"^[A-Za-z0-9\-_]+$")
                .WithMessage("Customer code can only contain letters, numbers, hyphens, and underscores");

            RuleFor(x => x.AddressLine1)
                .NotEmpty().WithMessage("Address line 1 is required")
                .MaximumLength(100).WithMessage("Address line 1 cannot exceed 100 characters");

            RuleFor(x => x.AddressLine2)
                .MaximumLength(100).WithMessage("Address line 2 cannot exceed 100 characters");

            RuleFor(x => x.City)
                .NotEmpty().WithMessage("City is required")
                .MaximumLength(50).WithMessage("City cannot exceed 50 characters");

            RuleFor(x => x.Postcode)
                .NotEmpty().WithMessage("Postcode is required")
                .MaximumLength(20).WithMessage("Postcode cannot exceed 20 characters");

            RuleFor(x => x.Telephone)
                .MaximumLength(20).WithMessage("Telephone cannot exceed 20 characters");

            RuleFor(x => x.Email)
                .MaximumLength(100).WithMessage("Email cannot exceed 100 characters")
                .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
                .WithMessage("Email must be a valid email address");
        }
    }
}
