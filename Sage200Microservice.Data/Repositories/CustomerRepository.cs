using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public class CustomerRepository : Repository<Customer>, ICustomerRepository
    {
        public CustomerRepository(ApplicationContext context) : base(context)
        {
        }

        public async Task<Customer> GetByCustomerCodeAsync(string customerCode)
        {
            return await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerCode == customerCode);
        }

        public async Task<Customer> GetByCodeAsync(string customerCode)
        {
            return await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerCode == customerCode);
        }
    }
}