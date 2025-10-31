using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service contract to create Sales Payments in Sage 200.
    /// </summary>
    public interface ISalesPaymentsService
    {
        /// <summary>
        /// Creates a Sales Payment (POST /sales_payments) and returns the URN on success.
        /// Uses omit-nulls JSON and forwards X-Site/X-Company headers.
        /// </summary>
        Task<SalesCreateResult> CreateAsync(SalesPaymentCreate request, HttpContext http, CancellationToken ct);
    }
}
