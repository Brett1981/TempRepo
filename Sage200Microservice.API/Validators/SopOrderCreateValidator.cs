using FluentValidation;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.API.Validators;

/// <summary>FluentValidation rules for SOP order create payload.</summary>
public sealed class SopOrderCreateValidator : AbstractValidator<SopOrderCreate>
{
    public SopOrderCreateValidator()
    {
        RuleFor(x => x.Header.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();

        RuleForEach(x => x.Lines).SetValidator(new LineValidator());
    }

    private sealed class LineValidator : AbstractValidator<SopOrderCreateLine>
    {
        public LineValidator()
        {
            RuleFor(x => x.ProductCode).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        }
    }
}
