using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public class InvoiceRepository : Repository<Invoice>, IInvoiceRepository
    {
        public InvoiceRepository(ApplicationContext context) : base(context)
        {
        }

        public async Task<Invoice> GetByReferenceAsync(string reference)
        {
            return await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceReference == reference);
        }

        public async Task<IEnumerable<Invoice>> GetOutstandingInvoicesAsync()
        {
            // Get invoices that are not fully paid
            return await _context.Invoices
                .Where(i => i.OutstandingValue > 0)
                .ToListAsync();
        }
    }
}