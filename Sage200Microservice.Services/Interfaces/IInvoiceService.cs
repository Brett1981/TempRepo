using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Invoice orchestration for Sage 200:
    /// - Creates Sales **Order** invoices against Sage (simple, header+lines).
    /// - Checks invoice status by reference and records status history locally.
    /// - Background processing of outstanding invoices.
    ///
    /// NOTE: Public signatures preserved to avoid breaking callers.
    /// </summary>
    public interface IInvoiceService
    {
        /// <summary>
        /// Creates a Sales Order in Sage and records a local Invoice row mapped to the returned reference.
        /// Returns the local DB identifier and the created order reference.
        /// </summary>
        /// <param name="invoice">Local invoice request (customer, values).</param>
        /// <param name="lines">Order lines to create in Sage.</param>
        /// <returns>(Success, Message, LocalOrderId, OrderReference)</returns>
        Task<(bool Success, string Message, long OrderId, string OrderReference)> CreateSalesOrderInvoiceAsync(Invoice invoice, List<OrderLine> lines);

        /// <summary>
        /// Looks up an invoice/transaction by reference in Sage and computes payment state.
        /// This implementation prefers the widely-available <c>sales_transaction_views</c> entity set.
        /// If that set is unavailable on a tenant (404), we fall back to <c>sales_invoices</c>
        /// and then <c>trader_transactions</c>. Any 400/404 is treated as "no rows" instead of an error.
        /// </summary>
        /// <param name="invoiceReference">Reference such as "INV-2025-003".</param>
        /// <returns>Tuple of status booleans, values, and allocation history items (may be empty).</returns>
        Task<(bool Success, string Message, bool IsPaid, bool IsCredited, decimal OutstandingValue, decimal AllocatedValue, List<SageAllocationHistoryItem> AllocationHistory)> CheckInvoiceStatusAsync(string invoiceReference);

        /// <summary>
        /// Scans all locally outstanding invoices and refreshes status from Sage, recording a history row per check.
        /// Any 400/404 from upstream is treated as "not available/no rows" and logged at Warning, not Error.
        /// </summary>
        Task ProcessOutstandingInvoicesAsync();
    }
}
