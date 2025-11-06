using FluentValidation;
using Sage200Microservice.API.Models.Customers;

namespace Sage200Microservice.API.Validators
{
    public sealed class GetCustomerDetailsQueryValidator : AbstractValidator<GetCustomerDetailsQuery>
    {
        public GetCustomerDetailsQueryValidator()
        {
            RuleFor(x => x.Page).GreaterThan(0);
            RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        }
    }
}
