using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Messaging.Consumers.Common;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models; // RequestContext
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging.Orchestration
{
    /// <summary>
    /// Coordinates the inbound MDM_INVOICES workflow:
    /// 1. Validates and normalizes the payload.
    /// 2. Ensures customer exists or creates one in Sage.
    /// 3. Creates SOP order (which generates the invoice).
    /// 4. Updates DB mappings (ExternalIdLink, TransactionAttempt).
    /// 5. Publishes a result event (Success/Failure) to MDM_INVOICE_RESULTS.
    /// </summary>
    public sealed class InvoiceRequestOrchestrator : IInvoiceRequestOrchestrator
    {
        private readonly ILogger<InvoiceRequestOrchestrator> _logger;
        private readonly ApplicationContext _db;
        private readonly ICustomerService _customerService;
        private readonly ISopOrderService _sopOrderService;
        private readonly ISalesInvoicesService _invoiceService;
        private readonly IEventPublisher _publisher;

        private static readonly JsonSerializerOptions _json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        public InvoiceRequestOrchestrator(
            ILogger<InvoiceRequestOrchestrator> logger,
            ApplicationContext db,
            ICustomerService customerService,
            ISopOrderService sopOrderService,
            ISalesInvoicesService invoiceService,
            IEventPublisher publisher)
        {
            _logger = logger;
            _db = db;
            _customerService = customerService;
            _sopOrderService = sopOrderService;
            _invoiceService = invoiceService;
            _publisher = publisher;
        }

        public async Task OrchestrateAsync(MdmInvoiceMessage message, RequestContext context, int apiKeyId, CancellationToken ct)
        {
            var attempt = await _db.TransactionAttempts
                .FirstOrDefaultAsync(x => x.CorrelationId == context.CorrelationId, ct)
                .ConfigureAwait(false);

            if (attempt is null)
                throw new InvalidOperationException("TransactionAttempt not found; should have been created by consumer.");

            attempt.ProcessingStatus = "SageProcessing";
            attempt.ResultMessage = "Processing invoice request...";
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = new ResultMessageEnvelope
            {
                CorrelationId = context.CorrelationId,
                IdempotencyKey = context.IdempotencyKey,
                ApiKeyId = apiKeyId,
                EntityType = ExternalEntityType.SalesInvoice,
                ExternalRef = message.ExternalRef,
                Status = "Failure"
            };

            try
            {
                // --- Step 1: Ensure / Upsert Customer ---
                var customer = message.Customer;
                var customerRef = customer.CustomerReference ?? message.ExternalRef ?? Guid.NewGuid().ToString("N");
                var sageCustomer = await _customerService.UpsertCustomerAsync(customer, context, ct)
                                        .ConfigureAwait(false);
                var customerUrn = sageCustomer.CustomerCode;
                var customerId = sageCustomer.SageCustomerId;

                // --- Step 2: Create SOP Order ---
                var sopOrder = await _sopOrderService.CreateSopOrderAsync(
                    message.SopOrder, customerUrn, context, ct).ConfigureAwait(false);

                var sopUrn = sopOrder.OrderReference;
                var sopId = sopOrder.OrderId;

                // --- Step 3: Generate Invoice from SOP ---
                var invoice = await _invoiceService.CreateInvoiceFromSopAsync(
                    sopUrn, context, ct).ConfigureAwait(false);

                result.SageUrn = invoice.Urn;
                //result.SageId = invoice.Id;
                result.Status = "Success";

                // --- Step 4: Update ExternalIdLink ---
                if (!string.IsNullOrWhiteSpace(message.ExternalRef))
                {
                    var entityType = ExternalEntityType.SalesInvoice;
                    var existing = await _db.ExternalIdLinks
                        .FirstOrDefaultAsync(x => x.AppId == apiKeyId && x.EntityType == entityType &&
                                                  x.ExternalRef == message.ExternalRef, ct)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        _db.ExternalIdLinks.Add(new ExternalIdLink
                        {
                            AppId = apiKeyId,
                            EntityType = entityType,
                            ExternalRef = message.ExternalRef,
                            SageUrn = invoice?.Urn,
                            //SageId = invoice?.Id,
                            CreatedUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.SageUrn = invoice?.Urn ?? existing.SageUrn;
                        existing.SageId = existing.SageId;
                    }
                }

                // --- Step 5: Update TransactionAttempt ---
                attempt.ProcessingStatus = "SageSuccess";
                attempt.SageUrn = invoice?.Urn;
                //attempt.SageId = invoice?.Id;
                attempt.ProcessingCompletedUtc = DateTime.UtcNow;
                attempt.DurationMs = (int?)(attempt.ProcessingCompletedUtc - attempt.ProcessingStartedUtc)?.TotalMilliseconds;
                attempt.ResultMessage = "Invoice successfully processed via Sage.";

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                // --- Step 6: Publish Result ---
                await PublishResultAsync(result, ct).ConfigureAwait(false);

                _logger.LogInformation("InvoiceRequestOrchestrator completed successfully: Correlation={CorrelationId}, URN={Urn}",
                    context.CorrelationId, invoice?.Urn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice orchestration failed for Correlation={CorrelationId}", context.CorrelationId);

                result.Status = "Failure";
                result.Errors = new List<ResultErrorItem>
                {
                    new() { Code = "EXCEPTION", Message = ex.Message }
                };

                attempt.ProcessingStatus = "SageFailure";
                attempt.ResultMessage = $"Failure: {ex.Message}";
                attempt.ProcessingCompletedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                await PublishResultAsync(result, ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task PublishResultAsync(ResultMessageEnvelope result, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(result, _json);
            await _publisher.PublishAsync("MDM_INVOICE_RESULTS", json, ct).ConfigureAwait(false);
        }
    }
}
