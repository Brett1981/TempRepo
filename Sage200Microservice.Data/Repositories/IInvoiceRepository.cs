using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public interface IInvoiceRepository : IRepository<Invoice>
    {
        Task<Invoice> GetByReferenceAsync(string reference);

        Task<IEnumerable<Invoice>> GetOutstandingInvoicesAsync();
    }
}