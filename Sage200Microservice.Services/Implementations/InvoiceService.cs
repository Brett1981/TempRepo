using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Production-hardened Invoice service:
    /// - Creates a sales order in Sage and persists a local invoice row.
    /// - Checks invoice status using a resilient search order:
    ///   sales_transaction_views → sales_invoices → trader_transactions.
    /// - Treats 400/404 from Sage as "no rows" to avoid noisy error logs.
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

        /// <summary>
        /// Creates a simple Sales Order in Sage and mirrors it locally as an Invoice record.
        /// </summary>
        public async Task<(bool Success, string Message, long OrderId, string OrderReference)> CreateSalesOrderInvoiceAsync(
            Invoice invoice,
            List<OrderLine> lines)
        {
            try
            {
                _logger.LogInformation("Creating sales order for customer {CustomerId} with {LineCount} lines",
                    invoice.CustomerId, lines.Count);

                // Build a minimal Sage Sales Order payload (adjust to match your Sage contract if needed).
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

                // Post to Sage – return as JsonDocument to be defensive about schema.
                var createdDoc = await _sageApiClient.PostAsync<object, JsonDocument>(
                    "sop_orders",
                    sageOrderRequest,
                    CancellationToken.None);

                // Extract id + reference from JSON (handles { value:[{...}] } and single object).
                var (sageId, orderRef) = ExtractOrderIdentity(createdDoc);

                if (sageId == 0 || string.IsNullOrWhiteSpace(orderRef))
                {
                    _logger.LogWarning("Sage returned an unexpected payload when creating order. JSON: {Json}", createdDoc.RootElement.GetRawText());
                    return (false, "Sage did not return a valid order id/reference.", 0, string.Empty);
                }

                // Persist local Invoice (mapped to the created Sage order)
                invoice.SageId = sageId;
                invoice.InvoiceReference = orderRef;
                invoice.IsSynced = true;
                invoice.CreatedAt = invoice.CreatedAt == default ? DateTime.UtcNow : invoice.CreatedAt;
                invoice.LastCheckedAt = DateTime.UtcNow;

                var savedInvoice = await _invoiceRepository.AddAsync(invoice);

                // Write an initial status history row
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

                await _statusHistoryRepository.AddAsync(statusHistory);

                _logger.LogInformation("Sales order created successfully. Reference {Ref}, SageId {Id}, LocalId {LocalId}",
                    orderRef, sageId, savedInvoice.Id);

                return (true, "Sales order created successfully", savedInvoice.Id, savedInvoice.InvoiceReference);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error creating sales order for customer {CustomerId}", invoice.CustomerId);
                return (false, $"HTTP error creating sales order: {ex.Message}", 0, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sales order for customer {CustomerId}", invoice.CustomerId);
                return (false, $"Error creating sales order: {ex.Message}", 0, string.Empty);
            }
        }

        /// <summary>
        /// Checks the payment/credit status of a referenced invoice using Sage.
        /// Prefer sales_transaction_views (broad coverage), then fall back to others.
        /// </summary>
        public async Task<(bool Success, string Message, bool IsPaid, bool IsCredited, decimal OutstandingValue, decimal AllocatedValue, List<SageAllocationHistoryItem> AllocationHistory)>
            CheckInvoiceStatusAsync(string invoiceReference)
        {
            try
            {
                _logger.LogInformation("Checking status of invoice {InvoiceReference}", invoiceReference);

                // --- Search Sage across multiple entity sets in priority order ---
                var eq = EscapeODataString(invoiceReference);
                var candidates = new[]
                {
                    // Prefer a trivial probe first (also warms the connection on some tenants)
                    $"sales_transaction_views?$top=1",

                    // Narrow by likely fields — no enum comparison to avoid type issues
                    $"sales_transaction_views?$filter=reference eq '{eq}'&$top=1",

                    // Fallback entity sets (may be disabled on some tenants)
                    $"sales_invoices?$filter=reference eq '{eq}'&$top=1",
                    $"trader_transactions?$filter=trader_reference eq '{eq}'&$top=1"
                };
                var doc = await TryFirstOkAsync(candidates);
                if (doc is null)
                {
                    _logger.LogWarning("Invoice '{Ref}' not found or entity sets unavailable on this tenant.", invoiceReference);
                    return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
                }

                // Snapshot essential fields from whatever shape we got back.
                var snap = MaterializeInvoiceSnapshot(doc);
                if (snap is null)
                    return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());

                var documentNo = !string.IsNullOrWhiteSpace(snap.DocumentNo) ? snap.DocumentNo : invoiceReference;
                var gross = snap.Gross ?? 0m;
                var outstanding = snap.Outstanding ?? 0m;
                var allocated = Math.Max(0m, gross - outstanding);

                // Upsert local invoice
                var invoice = await _invoiceRepository.GetByReferenceAsync(invoiceReference);
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
                    await _invoiceRepository.AddAsync(invoice);
                }
                else
                {
                    invoice.OutstandingValue = outstanding;
                    invoice.GrossValue = invoice.GrossValue == 0 ? gross : invoice.GrossValue; // keep if already set
                    invoice.Status = outstanding == 0m ? "Paid" : (outstanding < gross ? "PartiallyPaid" : "Unpaid");
                    invoice.LastCheckedAt = DateTime.UtcNow;
                    invoice.IsSynced = true;
                    await _invoiceRepository.UpdateAsync(invoice);
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
                await _statusHistoryRepository.AddAsync(statusHistory);

                // If you want allocation history, add a targeted query here (left empty by default).
                var allocHistory = new List<SageAllocationHistoryItem>();
                var isPaid = outstanding == 0m;
                var isCredited = snap.IsCredited ?? false;

                return (true, "Invoice status checked successfully", isPaid, isCredited, outstanding, allocated, allocHistory);
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
            {
                // Treat "bad query" or "entity set missing" as not-found, per tenant variability.
                _logger.LogWarning(ex, "Invoice '{Ref}' not found on Sage due to upstream {Code}.", invoiceReference, ex.StatusCode);
                return (false, "Invoice not found on Sage.", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking status of invoice {InvoiceReference}", invoiceReference);
                return (false, $"Error checking invoice status: {ex.Message}", false, false, 0m, 0m, new List<SageAllocationHistoryItem>());
            }
        }

        /// <summary>
        /// Processes all locally outstanding invoices and refreshes status from Sage.
        /// </summary>
        public async Task ProcessOutstandingInvoicesAsync()
        {
            try
            {
                _logger.LogInformation("Processing outstanding invoices...");
                var outstandingInvoices = await _invoiceRepository.GetOutstandingInvoicesAsync();
                _logger.LogInformation("Found {Count} outstanding invoices to process", outstandingInvoices.Count().ToString());

                foreach (var invoice in outstandingInvoices)
                {
                    try
                    {
                        var result = await CheckInvoiceStatusAsync(invoice.InvoiceReference);
                        if (!result.Success)
                        {
                            _logger.LogWarning("Status check failed for invoice {Ref}: {Msg}", invoice.InvoiceReference, result.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing invoice {Ref}", invoice.InvoiceReference);
                    }
                }

                _logger.LogInformation("Finished processing outstanding invoices.");
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

        /// <summary>
        /// Attempts each endpoint in order, returning the first JsonDocument that succeeds with 200.
        /// 400/404 => treated as "no rows/unsupported" and we try the next candidate.
        /// 5xx (or no StatusCode, e.g., socket/TLS) => transient; log+try next candidate after client-level retries.
        /// </summary>
        private async Task<JsonDocument?> TryFirstOkAsync(IEnumerable<string> endpoints)
        {
            foreach (var ep in endpoints)
            {
                try
                {
                    return await _sageApiClient.GetAsync<JsonDocument>(ep, CancellationToken.None);
                }
                catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
                {
                    _logger.LogDebug("Upstream {Code} for {Endpoint}; trying next.", ex.StatusCode, ep);
                }
                // Also skip over transient 5xx and network errors
                catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
                {
                    _logger.LogDebug("Upstream {Code} for {Endpoint}; trying next.", ex.StatusCode, ep);
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts (id, reference) from a create response. Handles both single-object and { "value": [ {...} ] }.
        /// </summary>
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

                // Many SOP orders expose "reference"; some use "document_no" or "order_no".
                if (el.TryGetProperty("reference", out var r1) && r1.ValueKind == JsonValueKind.String) reference = r1.GetString()!;
                else if (el.TryGetProperty("document_no", out var r2) && r2.ValueKind == JsonValueKind.String) reference = r2.GetString()!;
                else if (el.TryGetProperty("order_no", out var r3) && r3.ValueKind == JsonValueKind.String) reference = r3.GetString()!;

                return (id, reference);
            }

            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                {
                    return FromElement(arr[0]);
                }
                return FromElement(root);
            }

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                return FromElement(root[0]);
            }

            return (0L, string.Empty);
        }

        /// <summary>
        /// Given an arbitrary Sage transaction JSON response, returns a normalized snapshot of useful fields.
        /// Handles shapes from sales_transaction_views / sales_invoices / trader_transactions.
        /// </summary>
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
        private async Task<string?> PickExistingFieldAsync(string entity, params string[] candidates)
        {
            try
            {
                var doc = await _sageApiClient.GetAsync<JsonDocument>($"{entity}?$top=1", CancellationToken.None);
                var root = doc.RootElement;
                JsonElement first = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0
                    ? v[0]
                    : (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : default);

                if (first.ValueKind != JsonValueKind.Undefined && first.ValueKind != JsonValueKind.Null && first.ValueKind != JsonValueKind.Undefined)
                {
                    foreach (var name in candidates)
                    {
                        if (first.TryGetProperty(name, out _)) return name;
                    }
                }
            }
            catch { /* ignore and fall back */ }
            return null;
        }
        // Lightweight normalized view over various Sage response shapes
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
