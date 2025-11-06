using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Payments
{
    /// <summary>
    /// Abstraction for refreshing allocation values for a Sage Sales Invoice.
    /// Backed by ISageApiClient calling GET /api/sales-invoices/{invoiceUrn}.
    /// </summary>
    public interface IPaymentsAllocationService
    {
        /// <summary>
        /// Retrieves allocated/outstanding values and fully-allocated flag for a given invoice URN from Sage.
        /// </summary>
        /// <param name="invoiceUrn">Sage SalesInvoice URN.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Tuple of (allocated, outstanding, isFullyAllocated, statusMessage).
        /// statusMessage is null when successful.
        /// </returns>
        Task<(decimal? allocated, decimal? outstanding, bool? isFullyAllocated, string? statusMessage)>
            RefreshAllocationAsync(string invoiceUrn, CancellationToken ct);
    }
}
