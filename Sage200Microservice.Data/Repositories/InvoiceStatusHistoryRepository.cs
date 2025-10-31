using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public class InvoiceStatusHistoryRepository : Repository<InvoiceStatusHistory>, IInvoiceStatusHistoryRepository
    {
        public InvoiceStatusHistoryRepository(ApplicationContext context) : base(context)
        {
        }
    }
}