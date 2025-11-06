using Microsoft.AspNetCore.Http;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.Services.Interfaces
{
    public interface ISalesInvoicesService
    {
        Task<SalesCreateResult> CreateAsync(SalesInvoiceCreate request, RequestContext context, CancellationToken ct);

        Task<SalesCreateResult> CreateInvoiceFromSopAsync(string sopOrderUrn, Sage200Microservice.Services.Models.RequestContext context, CancellationToken ct);
    }

    public sealed class SalesInvoiceResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// URN returned by Sage.
        /// </summary>
        public string? Urn { get; set; }

        /// <summary>
        /// Typed failure (when Success == false) so controllers can map status codes.
        /// </summary>
        public FailureKind Failure { get; set; } = FailureKind.None;

        /// <summary>
        /// Optional upstream HTTP status (when Failure == Upstream).
        /// </summary>
        public int? UpstreamStatusCode { get; set; }

        /// <summary>
        /// Optional upstream body excerpt for diagnostics.
        /// </summary>
        public string? UpstreamBody { get; set; }
    }
}