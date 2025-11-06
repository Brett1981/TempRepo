using FluentValidation;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.API.Security;

namespace Sage200Microservice.API.Validators
{
    /// <summary>
    /// Validator for CreateApiKeyRequestDto
    /// </summary>
    public class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequestDto>
    {
        public CreateApiKeyRequestValidator()
        {
            RuleFor(x => x.ClientName)
                .NotEmpty().WithMessage("Client name is required")
                .Length(3, 100).WithMessage("Client name must be between 3 and 100 characters");

            RuleFor(x => x.ExpiresAt)
                .Must(BeInFuture).When(x => x.ExpiresAt.HasValue)
                .WithMessage("Expiration date must be in the future");

            RuleFor(x => x.AllowedIpAddresses)
                .Must(BeValidIpAddresses).When(x => !string.IsNullOrWhiteSpace(x.AllowedIpAddresses))
                .WithMessage("One or more IP addresses or CIDR ranges are invalid");
        }

        private static bool BeInFuture(DateTime? date) =>
            !date.HasValue || date.Value > DateTime.UtcNow;

        private static bool BeValidIpAddresses(string ipAddresses)
        {
            var ips = ipAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(ip => ip.Trim()).ToList();
            return IpAddressHelper.ValidateIpRanges(ips);
        }
    }

    /// <summary>
    /// Validator for UpdateApiKeyRequestDto
    /// </summary>
    public class UpdateApiKeyRequestValidator : AbstractValidator<UpdateApiKeyRequestDto>
    {
        public UpdateApiKeyRequestValidator()
        {
            RuleFor(x => x.ClientName)
                .NotEmpty().WithMessage("Client name is required")
                .Length(3, 100).WithMessage("Client name must be between 3 and 100 characters");

            RuleFor(x => x.ExpiresAt)
                .Must(BeInFuture).When(x => x.ExpiresAt.HasValue)
                .WithMessage("Expiration date must be in the future");

            RuleFor(x => x.AllowedIpAddresses)
                .Must(BeValidIpAddresses).When(x => !string.IsNullOrWhiteSpace(x.AllowedIpAddresses))
                .WithMessage("One or more IP addresses or CIDR ranges are invalid");
        }

        private static bool BeInFuture(DateTime? date) =>
            !date.HasValue || date.Value > DateTime.UtcNow;

        private static bool BeValidIpAddresses(string ipAddresses)
        {
            var ips = ipAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(ip => ip.Trim()).ToList();
            return IpAddressHelper.ValidateIpRanges(ips);
        }
    }

    /// <summary>
    /// Validator for RotateApiKeyRequestDto
    /// </summary>
    public class RotateApiKeyRequestValidator : AbstractValidator<RotateApiKeyRequestDto>
    {
        public RotateApiKeyRequestValidator()
        {
            RuleFor(x => x.GracePeriodDays)
                .InclusiveBetween(1, 30)
                .WithMessage("Grace period must be between 1 and 30 days");
        }
    }
}