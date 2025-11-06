// API/Validators/InvoiceValidators.cs
using FluentValidation;
using Sage200Microservice.API.DTOs;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// Validator for sales order invoice creation requests
    /// </summary>
    public class CreateSalesOrderInvoiceRequestValidator : AbstractValidator<CreateSalesOrderInvoiceRequestDto>
    {
        public CreateSalesOrderInvoiceRequestValidator()
        {
            RuleFor(x => x.CustomerId)
                .GreaterThan(0).WithMessage("Customer ID must be greater than 0");

            RuleFor(x => x.Lines)
                .NotEmpty().WithMessage("At least one order line is required");

            RuleForEach(x => x.Lines).SetValidator(new OrderLineRequestValidator());
        }
    }

    /// <summary>
    /// Validator for order line requests
    /// </summary>
    public class OrderLineRequestValidator : AbstractValidator<OrderLineRequestDto>
    {
        public OrderLineRequestValidator()
        {
            RuleFor(x => x.ProductCode)
                .NotEmpty().WithMessage("Product code is required")
                .MaximumLength(50).WithMessage("Product code cannot exceed 50 characters");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0");

            RuleFor(x => x.UnitPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Unit price must be greater than or equal to 0");
        }
    }
}
