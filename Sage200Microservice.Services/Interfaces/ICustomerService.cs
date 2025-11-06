// =========================================================================================================
// ICustomerService.cs — UPDATED CONTRACT
// - Adds an overload of CreateCustomerAsync that accepts HttpContext so we can forward X-Site/X-Company,
//   apply Idempotency-Key, and optionally publish a Kafka event. The original method remains for
//   backward compatibility and simply delegates to the new overload.
//   (Prev version here: :contentReference[oaicite:0]{index=0})
// =========================================================================================================

using Microsoft.AspNetCore.Http;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Customers;
using Sage200Microservice.Services.Models.Sage;
using Sage200Microservice.Services.Models.Sop;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Customer operations against Sage 200 & the local store.
    /// </summary>
    public interface ICustomerService
    {
        /// <summary>
        /// Legacy signature (kept for compatibility). Creates a customer in Sage (best-effort) and persists locally.
        /// </summary>
        Task<(bool Success, string Message, long CustomerId, string CustomerCode)>
            CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Preferred signature — same semantics as the legacy method, but lets the service forward incoming
        /// <c>X-Site</c>/<c>X-Company</c> and apply an <c>Idempotency-Key</c> to the upstream POST.
        /// </summary>
        Task<(bool Success, string Message, long CustomerId, string CustomerCode)>
            CreateCustomerAsync(Customer customer, HttpContext http, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rich composite details by customer code.
        /// </summary>
        Task<CustomerDetails> GetCustomerDetailsAsync(string customerCode, int page, int pageSize, CancellationToken ct = default);

        /// <summary>
        /// Resolve a Sage customer by code/reference.
        /// </summary>
        Task<SageCustomer> GetCustomerByCodeAsync(string customerCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures a customer exists in Sage:
        /// - If found by code/reference → optionally do a minimal update (if you want),
        /// - Else create it.
        /// Returns Sage customer id and the canonical customer code/reference.
        /// </summary>
        Task<(bool Success, string Message, long? SageCustomerId, string CustomerCode)>
            UpsertCustomerAsync(Customer customer, HttpContext http, CancellationToken ct = default);

        Task<(bool Success, string Message, long? SageCustomerId, string CustomerCode)>
            UpsertCustomerAsync(CustomerPayload customer, Sage200Microservice.Services.Models.RequestContext context, CancellationToken ct = default);

        /// <summary>
        /// Creates a SOP order (and, if your flow requires, generates an invoice).
        /// Returns SOP order id/ref and the invoice URN if generated.
        /// </summary>
        Task<(bool Success, string Message, long? SopOrderId, string? SopOrderRef, string? SalesInvoiceUrn)>
            CreateInvoiceFromSopAsync(SopOrderCreate sopOrder, HttpContext http, CancellationToken ct = default);

    }
}
