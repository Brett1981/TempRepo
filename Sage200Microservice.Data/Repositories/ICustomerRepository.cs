using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<Customer> GetByCustomerCodeAsync(string customerCode);

        Task<Customer> GetByCodeAsync(string customerCode);
    }
}