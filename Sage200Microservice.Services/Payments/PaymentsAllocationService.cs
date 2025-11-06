using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Payments
{
    /// <summary>
    /// Default implementation of <see cref="IPaymentsAllocationService"/> using <see cref="ISageApiClient"/>.
    /// </summary>
    public sealed class PaymentsAllocationService : IPaymentsAllocationService
    {
        private readonly ISageApiClient _sageClient;
        private readonly ILogger<PaymentsAllocationService> _logger;

        /// <summary>
        /// Creates a new instance of <see cref="PaymentsAllocationService"/>.
        /// </summary>
        public PaymentsAllocationService(ISageApiClient sageClient, ILogger<PaymentsAllocationService> logger)
        {
            _sageClient = sageClient;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<(decimal? allocated, decimal? outstanding, bool? isFullyAllocated, string? statusMessage)>
            RefreshAllocationAsync(string invoiceUrn, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(invoiceUrn))
                return (null, null, null, "Missing invoice URN");

            try
            {
                // Contract you confirmed:
                // GET /api/sales-invoices/{invoiceUrn} → { allocatedValue, outstandingValue, isFullyAllocated }
                var path = $"/api/sales-invoices/{Uri.EscapeDataString(invoiceUrn)}";

                // We assume ISageApiClient exposes a generic GET; if not, adapt to your concrete client method.
                var dto = await _sageClient.GetAsync<SageSalesInvoiceAllocationDto>(path, ct);

                return (dto?.AllocatedValue, dto?.OutstandingValue, dto?.IsFullyAllocated, null);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Sage allocation lookup failed for URN {Urn}", invoiceUrn);
                return (null, null, null, $"Sage HTTP error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching Sage allocation for URN {Urn}", invoiceUrn);
                return (null, null, null, $"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Minimal projection of the Sage invoice payload needed for allocation checks.
        /// </summary>
        private sealed class SageSalesInvoiceAllocationDto
        {
            public string? Urn { get; set; }
            public decimal? AllocatedValue { get; set; }
            public decimal? OutstandingValue { get; set; }
            public bool? IsFullyAllocated { get; set; }
        }
    }
}
