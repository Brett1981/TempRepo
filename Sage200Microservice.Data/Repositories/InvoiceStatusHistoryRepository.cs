using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Extensions;

namespace Sage200Microservice.Data.Repositories
{
    public class InvoiceStatusHistoryRepository : Repository<InvoiceStatusHistory>, IInvoiceStatusHistoryRepository
    {
        public InvoiceStatusHistoryRepository(ApplicationContext context) : base(context)
        {
        }
    }
}