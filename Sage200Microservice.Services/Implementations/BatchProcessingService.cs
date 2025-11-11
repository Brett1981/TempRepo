using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using System.Threading;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Service for batch processing operations
    /// </summary>
    public class BatchProcessingService : IBatchProcessingService
    {
        private readonly IInvoiceService _invoiceService;
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IInvoiceStatusHistoryRepository _statusHistoryRepository;
        private readonly ILogger<BatchProcessingService> _logger;

        public BatchProcessingService(
            IInvoiceService invoiceService,
            IInvoiceRepository invoiceRepository,
            IInvoiceStatusHistoryRepository statusHistoryRepository,
            ILogger<BatchProcessingService> logger)
        {
            _invoiceService = invoiceService;
            _invoiceRepository = invoiceRepository;
            _statusHistoryRepository = statusHistoryRepository;
            _logger = logger;
        }

        public async Task<BatchProcessingResult> ProcessInvoiceBatchAsync(
            IEnumerable<string> invoiceReferences,
            int batchSize = 100,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5,
            CancellationToken ct = default)
        {
            var result = new BatchProcessingResult
            {
                TotalItems = invoiceReferences.Count(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("Starting batch processing of {Count} invoices", result.TotalItems);
                ct.ThrowIfCancellationRequested();

                var batches = invoiceReferences
                    .Select((reference, index) => new { reference, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.reference).ToList())
                    .ToList();

                result.BatchCount = batches.Count;
                _logger.LogInformation("Split into {BatchCount} batches of size {BatchSize}", result.BatchCount, batchSize);

                foreach (var batch in batches)
                {
                    ct.ThrowIfCancellationRequested();

                    if (parallelProcessing)
                    {
                        var parallelOptions = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDegreeOfParallelism,
                            CancellationToken = ct
                        };

                        await Parallel.ForEachAsync(batch, parallelOptions, async (invoiceReference, token) =>
                        {
                            await ProcessSingleInvoiceAsync(invoiceReference, result, token);
                        });
                    }
                    else
                    {
                        foreach (var invoiceReference in batch)
                        {
                            ct.ThrowIfCancellationRequested();
                            await ProcessSingleInvoiceAsync(invoiceReference, result, ct);
                        }
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = true;
                result.Message = $"Successfully processed {result.SuccessCount} out of {result.TotalItems} invoices";

                _logger.LogInformation("Batch processing completed: {Message}", result.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = false;
                result.Message = "Batch processing cancelled.";
                _logger.LogWarning("Batch processing was cancelled.");
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = false;
                result.Message = $"Error during batch processing: {ex.Message}";
                result.ErrorDetails = ex.ToString();

                _logger.LogError(ex, "Error during batch processing");
            }

            return result;
        }

        public async Task<BatchProcessingResult> CreateInvoiceBatchAsync(
            IEnumerable<(Invoice Invoice, List<Models.OrderLine> Lines)> invoices,
            int batchSize = 50,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5,
            CancellationToken ct = default)
        {
            var result = new BatchProcessingResult
            {
                TotalItems = invoices.Count(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("Starting batch creation of {Count} invoices", result.TotalItems);
                ct.ThrowIfCancellationRequested();

                var batches = invoices
                    .Select((item, index) => new { item, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.item).ToList())
                    .ToList();

                result.BatchCount = batches.Count;
                _logger.LogInformation("Split into {BatchCount} batches of size {BatchSize}", result.BatchCount, batchSize);

                foreach (var batch in batches)
                {
                    ct.ThrowIfCancellationRequested();

                    if (parallelProcessing)
                    {
                        var parallelOptions = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDegreeOfParallelism,
                            CancellationToken = ct
                        };

                        await Parallel.ForEachAsync(batch, parallelOptions, async (item, token) =>
                        {
                            await CreateSingleInvoiceAsync(item.Invoice, item.Lines, result, token);
                        });
                    }
                    else
                    {
                        foreach (var item in batch)
                        {
                            ct.ThrowIfCancellationRequested();
                            await CreateSingleInvoiceAsync(item.Invoice, item.Lines, result, ct);
                        }
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = true;
                result.Message = $"Successfully created {result.SuccessCount} out of {result.TotalItems} invoices";

                _logger.LogInformation("Batch creation completed: {Message}", result.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = false;
                result.Message = "Batch creation cancelled.";
                _logger.LogWarning("Batch creation was cancelled.");
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = false;
                result.Message = $"Error during batch creation: {ex.Message}";
                result.ErrorDetails = ex.ToString();

                _logger.LogError(ex, "Error during batch creation");
            }

            return result;
        }

        // -------- internals --------

        private async Task ProcessSingleInvoiceAsync(string invoiceReference, BatchProcessingResult result, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var correlationId = Guid.NewGuid().ToString();

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["InvoiceReference"] = invoiceReference,
                    ["CorrelationId"] = correlationId
                }))
                {
                    _logger.LogInformation("Processing invoice {InvoiceReference}", invoiceReference);

                    // Pass CT through to service (so outbound Sage calls can be cancelled)
                    var statusResult = await _invoiceService.CheckInvoiceStatusAsync(invoiceReference, ct);

                    if (statusResult.Success)
                    {
                        _logger.LogInformation("Processed {InvoiceReference}: Status={Status}, Outstanding={Outstanding}",
                            invoiceReference, statusResult.IsPaid ? "Paid" : "Not Paid", statusResult.OutstandingValue);

                        // Optional repository calls; add ct if your repo supports it.
                        var invoice = await _invoiceRepository.GetByReferenceAsync(invoiceReference);
                        if (invoice != null)
                        {
                            var statusHistory = new InvoiceStatusHistory
                            {
                                InvoiceReference = invoiceReference,
                                GrossValue = invoice.GrossValue,
                                OutstandingValue = statusResult.OutstandingValue,
                                AllocatedValue = statusResult.AllocatedValue,
                                Status = statusResult.IsPaid
                                    ? "Paid"
                                    : (statusResult.OutstandingValue < invoice.GrossValue ? "PartiallyPaid" : "Unpaid"),
                                CheckTimestamp = DateTime.UtcNow,
                                Source = "BatchProcessing",
                                CheckedBy = "BatchService",
                                CorrelationId = correlationId
                            };

                            await _statusHistoryRepository.AddAsync(statusHistory);
                        }

                        lock (result)
                        {
                            result.SuccessCount++;
                            result.ProcessedItems.Add(new BatchProcessingItem
                            {
                                ItemId = invoiceReference,
                                Success = true,
                                Message = $"Successfully processed invoice {invoiceReference}"
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to process {InvoiceReference}: {Message}", invoiceReference, statusResult.Message);

                        lock (result)
                        {
                            result.FailureCount++;
                            result.ProcessedItems.Add(new BatchProcessingItem
                            {
                                ItemId = invoiceReference,
                                Success = false,
                                Message = $"Failed to process invoice {invoiceReference}: {statusResult.Message}"
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Processing cancelled for {InvoiceReference}", invoiceReference);
                lock (result)
                {
                    result.FailureCount++;
                    result.ProcessedItems.Add(new BatchProcessingItem
                    {
                        ItemId = invoiceReference,
                        Success = false,
                        Message = "Cancelled"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invoice {InvoiceReference}", invoiceReference);
                lock (result)
                {
                    result.FailureCount++;
                    result.ProcessedItems.Add(new BatchProcessingItem
                    {
                        ItemId = invoiceReference,
                        Success = false,
                        Message = $"Error processing invoice {invoiceReference}: {ex.Message}",
                        ErrorDetails = ex.ToString()
                    });
                }
            }
        }

        private async Task CreateSingleInvoiceAsync(Invoice invoice, List<Models.OrderLine> lines, BatchProcessingResult result, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var correlationId = Guid.NewGuid().ToString();

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["InvoiceReference"] = invoice.InvoiceReference,
                    ["CustomerId"] = invoice.CustomerId,
                    ["CorrelationId"] = correlationId
                }))
                {
                    _logger.LogInformation("Creating invoice {InvoiceReference} for customer {CustomerId}", invoice.InvoiceReference, invoice.CustomerId);

                    // Pass CT through to service (so outbound Sage calls can be cancelled)
                    var createResult = await _invoiceService.CreateSalesOrderInvoiceAsync(invoice, lines, ct);

                    if (createResult.Success)
                    {
                        _logger.LogInformation("Created {InvoiceReference}: OrderId={OrderId}, OrderRef={OrderReference}",
                            invoice.InvoiceReference, createResult.OrderId, createResult.OrderReference);

                        lock (result)
                        {
                            result.SuccessCount++;
                            result.ProcessedItems.Add(new BatchProcessingItem
                            {
                                ItemId = invoice.InvoiceReference,
                                Success = true,
                                Message = $"Successfully created invoice {invoice.InvoiceReference}"
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to create {InvoiceReference}: {Message}", invoice.InvoiceReference, createResult.Message);

                        lock (result)
                        {
                            result.FailureCount++;
                            result.ProcessedItems.Add(new BatchProcessingItem
                            {
                                ItemId = invoice.InvoiceReference,
                                Success = false,
                                Message = $"Failed to create invoice {invoice.InvoiceReference}: {createResult.Message}"
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Creation cancelled for {InvoiceReference}", invoice.InvoiceReference);
                lock (result)
                {
                    result.FailureCount++;
                    result.ProcessedItems.Add(new BatchProcessingItem
                    {
                        ItemId = invoice.InvoiceReference,
                        Success = false,
                        Message = "Cancelled"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating invoice {InvoiceReference}", invoice.InvoiceReference);

                lock (result)
                {
                    result.FailureCount++;
                    result.ProcessedItems.Add(new BatchProcessingItem
                    {
                        ItemId = invoice.InvoiceReference,
                        Success = false,
                        Message = $"Error creating invoice {invoice.InvoiceReference}: {ex.Message}",
                        ErrorDetails = ex.ToString()
                    });
                }
            }
        }
    }
}
