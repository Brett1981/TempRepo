using System.Threading;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Invoice orchestration for Sage 200.
    /// </summary>
    public interface IInvoiceService
    {
        /// <summary>
        /// Creates a Sales Order in Sage and records a local Invoice row mapped to the returned reference.
        /// Returns the local DB identifier and the created order reference.
        /// </summary>
        Task<(bool Success, string Message, long OrderId, string OrderReference)> CreateSalesOrderInvoiceAsync(
            Invoice invoice,
            List<OrderLine> lines,
            CancellationToken ct = default);

        /// <summary>
        /// Looks up an invoice/transaction by reference in Sage and computes payment state.
        /// </summary>
        Task<(bool Success, string Message, bool IsPaid, bool IsCredited, decimal OutstandingValue, decimal AllocatedValue, List<SageAllocationHistoryItem> AllocationHistory)>
            CheckInvoiceStatusAsync(string invoiceReference, CancellationToken ct = default);

        /// <summary>
        /// Scans all locally outstanding invoices and refreshes status from Sage.
        /// </summary>
        Task ProcessOutstandingInvoicesAsync(CancellationToken ct = default);
    }
}
