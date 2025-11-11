using System.Threading;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public interface IInvoiceRepository : IRepository<Invoice>
    {
        // Optional CTs keep existing callers compiling, but let new code flow tokens through.
        Task<Invoice> GetByReferenceAsync(string reference, CancellationToken ct = default);

        Task<IEnumerable<Invoice>> GetOutstandingInvoicesAsync(CancellationToken ct = default);
    }
}
