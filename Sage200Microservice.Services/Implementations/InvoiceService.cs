using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System.Threading;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Production-hardened Invoice service:
    /// - Creates a sales order in Sage and persists a local invoice row.
    /// - Checks invoice status using a resilient search order:
    ///   sales_transaction_views → sales_invoices → trader_transactions.
    /// - Treats 400/404 from Sage as "no rows".
    /// - Records a status history entry on each check.
    /// </summary>
    public class InvoiceService : IInvoiceService
    {
        private readonly ILogger<InvoiceService> _logger;
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IInvoiceStatusHistoryRepository _statusHistoryRepository;
        private readonly ISageApiClient _sageApiClient;

        public InvoiceService(
            ILogger<InvoiceService> logger,
            IInvoiceRepository invoiceRepository,
            IInvoiceStatusHistoryRepository statusHistoryRepository,
            ISageApiClient sageApiClient)
        {
            _logger = logger;
            _invoiceRepository = invoiceRepository;
            _statusHistoryRepository = statusHistoryRepository;
            _sageApiClient = sageApiClient;
        }

        public async Task<(bool Success, string Message, long OrderId, string OrderReference)> CreateSalesOrderInvoiceAsync(
            Invoice invoice,
            List<OrderLine> lines,
            CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Creating sales order for customer {CustomerId} with {LineCount} lines",
                    invoice.CustomerId, lines.Count);

                ct.ThrowIfCancellationRequested();

                var sageOrderRequest = new
                {
                    customer_id = invoice.CustomerId,
                    order_date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    sop_order_lines = lines.Select(l => new
                    {
                        product_code = l.ProductCode,
                        quantity = l.Quantity,
                        unit_price = l.UnitPrice,
                        description = l.Description
                    }).ToArray()
                };

                // NOTE: ct propagated to HttpClient → Sage
                var createdDoc = await _sageApiClient.PostAsync<object, JsonDocument>(
                    "sop_orders", sageOrderRequest, ct);

                var (sageId, orderRef) = ExtractOrderIdentity(createdDoc);

                if (sageId == 0 || string.IsNullOrWhiteSpace(orderRef))
                {
                    _logger.LogWarning("Sage returned an unexpected payload when creating order. JSON: {Json}", createdDoc.RootElement.GetRawText());
                    return (false, "Sage did not return a valid order id/reference.", 0, string.Empty);
                }

                // Persist local Invoice (mapped to created Sage order)
                invoice.SageId = sageId;
                invoice.InvoiceReference = orderRef;
                invoice.IsSynced = true;
                invoice.CreatedAt = invoice.CreatedAt == default ? DateTime.UtcNow : invoice.CreatedAt;
                invoice.LastCheckedAt = DateTime.UtcNow;

                var savedInvoice = await _invoiceRepository.AddAsync(invoice); // add ct overload if available

                var statusHistory = new InvoiceStatusHistory
                {
                    InvoiceReference = orderRef,
                    GrossValue = invoice.GrossValue,
                    OutstandingValue = invoice.OutstandingValue,
                    AllocatedValue = 0,
                    Status = invoice.Status,
                    CheckTimestamp = DateTime.UtcNow,
                    Source = "Creation",
                    CheckedBy = invoice.CreatedBy ?? "System",
                    CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString()
                };

                await _statusHistoryRepository.AddAsync(statusHistory); // add ct overload if available

                _logger.LogInformation("Sales order created. Ref {Ref}, SageId {Id}, LocalId {LocalId}",
                    orderRef, sageId, savedInvoice.Id);

                return (true, "Sales order created successfully", savedInvoice.Id, savedInvoice.InvoiceReference);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error creating sales order for customer {CustomerId}", invoice.CustomerId);
                return (false, $"HTTP error creating sales order: {ex.Message}", 0, string.Empty);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (false, "Cancelled", 0, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sales order for customer {CustomerId}", invoice.CustomerId);
                return (false, $"Error creating sales order: {ex.Message}", 0, string.Empty);
            }
        }

        public async Task<(bool Success, string Message, bool IsPaid, bool IsCredited, decimal OutstandingValue, decimal AllocatedValue, List<SageAllocationHistoryItem> AllocationHistory)>
            CheckInvoiceStatusAsync(string invoiceReference, CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Checking status of invoice {InvoiceReference}", invoiceReference);
                ct.ThrowIfCancellationRequested();

                var eq = EscapeODataString(invoiceReference);
                var candidates = new[]
                {
                    "sales_transaction_views?$top=1",
                    $"sales_transaction_views?$filter=reference eq '{eq}'&$top=1",
                    $"sales_invoices?$filter=reference eq '{eq}'&$top=1",
                    $"trader_transactions?$filter=trader_reference eq '{eq}'&$top=1"
                };

                var doc = await TryFirstOkAsync(candidates, ct);
                if (doc is null)
                {
                    _logger.LogWarning("Invoice '{Ref}' not found or entity sets unavailable on this tenant.", invoiceReference);
                    return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
                }

                var snap = MaterializeInvoiceSnapshot(doc);
                if (snap is null)
                    return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());

                var documentNo = !string.IsNullOrWhiteSpace(snap.DocumentNo) ? snap.DocumentNo : invoiceReference;
                var gross = snap.Gross ?? 0m;
                var outstanding = snap.Outstanding ?? 0m;
                var allocated = Math.Max(0m, gross - outstanding);

                // Upsert local invoice
                var invoice = await _invoiceRepository.GetByReferenceAsync(invoiceReference); // add ct overload if available
                if (invoice == null)
                {
                    _logger.LogInformation("Invoice {Ref} not found locally; creating record.", invoiceReference);
                    invoice = new Invoice
                    {
                        InvoiceReference = documentNo,
                        SageId = snap.Id ?? 0,
                        CustomerId = (int)(snap.CustomerId ?? 0),
                        GrossValue = gross,
                        OutstandingValue = outstanding,
                        Status = outstanding == 0m ? "Paid" : (outstanding < gross ? "PartiallyPaid" : "Unpaid"),
                        CreatedAt = snap.CreatedAt ?? DateTime.UtcNow,
                        LastCheckedAt = DateTime.UtcNow,
                        CreatedBy = "System",
                        IsSynced = true
                    };
                    await _invoiceRepository.AddAsync(invoice); // add ct overload if available
                }
                else
                {
                    invoice.OutstandingValue = outstanding;
                    invoice.GrossValue = invoice.GrossValue == 0 ? gross : invoice.GrossValue;
                    invoice.Status = outstanding == 0m ? "Paid" : (outstanding < gross ? "PartiallyPaid" : "Unpaid");
                    invoice.LastCheckedAt = DateTime.UtcNow;
                    invoice.IsSynced = true;
                    await _invoiceRepository.UpdateAsync(invoice); // add ct overload if available
                }

                // Record history
                var statusHistory = new InvoiceStatusHistory
                {
                    InvoiceReference = documentNo,
                    GrossValue = gross,
                    OutstandingValue = outstanding,
                    AllocatedValue = allocated,
                    Status = invoice.Status,
                    CheckTimestamp = DateTime.UtcNow,
                    Source = "Manual",
                    CheckedBy = "System",
                    CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString()
                };
                await _statusHistoryRepository.AddAsync(statusHistory); // add ct overload if available

                var allocHistory = new List<SageAllocationHistoryItem>();
                var isPaid = outstanding == 0m;
                var isCredited = snap.IsCredited ?? false;

                return (true, "Invoice status checked successfully", isPaid, isCredited, outstanding, allocated, allocHistory);
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
            {
                _logger.LogWarning(ex, "Invoice '{Ref}' not found on Sage due to upstream {Code}.", invoiceReference, ex.StatusCode);
                return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (false, "Cancelled", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking status of invoice {InvoiceReference}", invoiceReference);
                return (false, $"Error checking invoice status: {ex.Message}", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
            }
        }

        public async Task ProcessOutstandingInvoicesAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Processing outstanding invoices...");
                ct.ThrowIfCancellationRequested();

                var outstandingInvoices = await _invoiceRepository.GetOutstandingInvoicesAsync(); // add ct overload if available
                _logger.LogInformation("Found {Count} outstanding invoices to process", outstandingInvoices.Count().ToString());

                foreach (var invoice in outstandingInvoices)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var result = await CheckInvoiceStatusAsync(invoice.InvoiceReference, ct);
                        if (!result.Success)
                        {
                            _logger.LogWarning("Status check failed for invoice {Ref}: {Msg}", invoice.InvoiceReference, result.Message);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing invoice {Ref}", invoice.InvoiceReference);
                    }
                }

                _logger.LogInformation("Finished processing outstanding invoices.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Outstanding invoices processing was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outstanding invoices");
                throw;
            }
        }

        // ======================================================================
        // Internal helpers
        // ======================================================================

        private async Task<JsonDocument?> TryFirstOkAsync(IEnumerable<string> endpoints, CancellationToken ct)
        {
            foreach (var ep in endpoints)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await _sageApiClient.GetAsync<JsonDocument>(ep, ct);
                }
                catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
                {
                    _logger.LogDebug("Upstream {Code} for {Endpoint}; trying next.", ex.StatusCode, ep);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
                {
                    _logger.LogDebug("Upstream {Code} for {Endpoint}; trying next.", ex.StatusCode, ep);
                }
            }
            return null;
        }

        private static (long Id, string Reference) ExtractOrderIdentity(JsonDocument doc)
        {
            static (long, string) FromElement(JsonElement el)
            {
                long id = 0;
                string reference = string.Empty;

                if (el.TryGetProperty("id", out var idProp))
                {
                    if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var i64)) id = i64;
                    else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var iStr)) id = iStr;
                }

                if (el.TryGetProperty("reference", out var r1) && r1.ValueKind == JsonValueKind.String) reference = r1.GetString()!;
                else if (el.TryGetProperty("document_no", out var r2) && r2.ValueKind == JsonValueKind.String) reference = r2.GetString()!;
                else if (el.TryGetProperty("order_no", out var r3) && r3.ValueKind == JsonValueKind.String) reference = r3.GetString()!;

                return (id, reference);
            }

            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    return FromElement(arr[0]);

                return FromElement(root);
            }

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                return FromElement(root[0]);

            return (0L, string.Empty);
        }

        private static InvoiceSnapshot? MaterializeInvoiceSnapshot(JsonDocument doc)
        {
            JsonElement obj;

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("value", out var v)
                && v.ValueKind == JsonValueKind.Array
                && v.GetArrayLength() > 0)
            {
                obj = v[0];
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                obj = doc.RootElement[0];
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                obj = doc.RootElement;
            }
            else
            {
                return null;
            }

            long? id = TryGetInt64(obj, "id");
            long? customerId = TryGetInt64(obj, "customer_id", "trader_id");
            string? documentNo = TryGetString(obj, "document_no", "reference", "trader_reference");
            DateTime? createdAt = TryGetDate(obj, "order_date", "posted_date", "document_date");
            decimal? gross = TryGetDecimal(obj, "document_gross_value", "gross_value", "base_gross_value", "gross");
            decimal? outstanding = TryGetDecimal(obj, "document_outstanding_value", "outstanding_value", "base_outstanding_value", "outstanding");
            bool? isCredited = TryGetString(obj, "transaction_type", "type")?.Equals("CreditNote", StringComparison.OrdinalIgnoreCase) ?? false;

            return new InvoiceSnapshot
            {
                Id = id,
                CustomerId = customerId,
                DocumentNo = documentNo,
                CreatedAt = createdAt,
                Gross = gross,
                Outstanding = outstanding,
                IsCredited = isCredited
            };
        }

        private static string EscapeODataString(string value)
            => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        private static string? TryGetString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            }
            return null;
        }

        private static long? TryGetInt64(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var i)) return i;
                    if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var i2)) return i2;
                }
            }
            return null;
        }

        private static decimal? TryGetDecimal(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
                    if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var d2)) return d2;
                }
            }
            return null;
        }

        private static DateTime? TryGetDate(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(p.GetString(), out var dt)) return dt;
                }
            }
            return null;
        }

        private sealed class InvoiceSnapshot
        {
            public long? Id { get; init; }
            public long? CustomerId { get; init; }
            public string? DocumentNo { get; init; }
            public DateTime? CreatedAt { get; init; }
            public decimal? Gross { get; init; }
            public decimal? Outstanding { get; init; }
            public bool? IsCredited { get; init; }
        }
    }
}
