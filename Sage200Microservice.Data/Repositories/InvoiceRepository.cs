using System.Threading;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public class InvoiceRepository : Repository<Invoice>, IInvoiceRepository
    {
        public InvoiceRepository(ApplicationContext context) : base(context) { }

        public async Task<Invoice> GetByReferenceAsync(string reference, CancellationToken ct = default)
        {
            // tracking is useful here in case the caller updates the entity afterwards
            return await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceReference == reference, ct);
        }

        public async Task<IEnumerable<Invoice>> GetOutstandingInvoicesAsync(CancellationToken ct = default)
        {
            // read-only query → AsNoTracking for perf
            return await _context.Invoices
                .AsNoTracking()
                .Where(i => i.OutstandingValue > 0)
                .ToListAsync(ct);
        }
    }
}
