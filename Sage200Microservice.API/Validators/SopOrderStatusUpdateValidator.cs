using FluentValidation;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// FluentValidation rules for SOP Order Status updates.
    /// </summary>
    public sealed class SopOrderStatusUpdateValidator : AbstractValidator<SopOrderStatusUpdate>
    {
        private static readonly string[] Allowed =
        {
            "Live", "OnHold", "Cancelled", "Canceled", "Completed", "Complete",
            // also allow Sage literal to pass-through (EnumDocumentStatus*)
        };

        public SopOrderStatusUpdateValidator()
        {
            RuleFor(x => x.OrderId)
                .GreaterThan(0);

            RuleFor(x => x.Status)
                .NotEmpty()
                .Must(BeAllowed)
                .WithMessage("Status must be one of: Live, OnHold, Cancelled, Completed or a valid Sage literal (EnumDocumentStatus*).");
        }

        private static bool BeAllowed(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            if (Allowed.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase)) return true;

            // allow direct Sage literal
            return status.StartsWith("EnumDocumentStatus", StringComparison.OrdinalIgnoreCase);
        }
    }
}
