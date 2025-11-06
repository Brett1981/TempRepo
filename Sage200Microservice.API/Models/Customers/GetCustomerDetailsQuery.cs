namespace Sage200Microservice.API.Models.Customers
{
    public sealed class GetCustomerDetailsQuery
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
