using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;

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

        /// <summary>
        /// Initializes a new instance of the BatchProcessingService class
        /// </summary>
        /// <param name="invoiceService">          The invoice service </param>
        /// <param name="invoiceRepository">       The invoice repository </param>
        /// <param name="statusHistoryRepository"> The status history repository </param>
        /// <param name="logger">                  The logger </param>
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

        /// <summary>
        /// Processes a batch of invoices
        /// </summary>
        /// <param name="invoiceReferences">      The invoice references to process </param>
        /// <param name="batchSize">              The batch size </param>
        /// <param name="parallelProcessing">     Whether to process in parallel </param>
        /// <param name="maxDegreeOfParallelism"> The maximum degree of parallelism </param>
        /// <returns> The batch processing result </returns>
        public async Task<BatchProcessingResult> ProcessInvoiceBatchAsync(
            IEnumerable<string> invoiceReferences,
            int batchSize = 100,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5)
        {
            var result = new BatchProcessingResult
            {
                TotalItems = invoiceReferences.Count(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("Starting batch processing of {Count} invoices", result.TotalItems);

                // Process invoices in batches
                var batches = invoiceReferences
                    .Select((reference, index) => new { Reference = reference, Index = index })
                    .GroupBy(x => x.Index / batchSize)
                    .Select(g => g.Select(x => x.Reference).ToList())
                    .ToList();

                result.BatchCount = batches.Count;

                _logger.LogInformation("Split into {BatchCount} batches of size {BatchSize}", result.BatchCount, batchSize);

                foreach (var batch in batches)
                {
                    if (parallelProcessing)
                    {
                        // Process batch in parallel
                        var parallelOptions = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDegreeOfParallelism
                        };

                        await Parallel.ForEachAsync(batch, parallelOptions, async (invoiceReference, token) =>
                        {
                            await ProcessSingleInvoiceAsync(invoiceReference, result);
                        });
                    }
                    else
                    {
                        // Process batch sequentially
                        foreach (var invoiceReference in batch)
                        {
                            await ProcessSingleInvoiceAsync(invoiceReference, result);
                        }
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = true;
                result.Message = $"Successfully processed {result.SuccessCount} out of {result.TotalItems} invoices";

                _logger.LogInformation("Batch processing completed: {Message}", result.Message);
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

        /// <summary>
        /// Creates a batch of invoices
        /// </summary>
        /// <param name="invoices">               The invoices to create </param>
        /// <param name="batchSize">              The batch size </param>
        /// <param name="parallelProcessing">     Whether to process in parallel </param>
        /// <param name="maxDegreeOfParallelism"> The maximum degree of parallelism </param>
        /// <returns> The batch processing result </returns>
        public async Task<BatchProcessingResult> CreateInvoiceBatchAsync(
            IEnumerable<(Invoice Invoice, List<Models.OrderLine> Lines)> invoices,
            int batchSize = 50,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5)
        {
            var result = new BatchProcessingResult
            {
                TotalItems = invoices.Count(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("Starting batch creation of {Count} invoices", result.TotalItems);

                // Process invoices in batches
                var batches = invoices
                    .Select((item, index) => new { Item = item, Index = index })
                    .GroupBy(x => x.Index / batchSize)
                    .Select(g => g.Select(x => x.Item).ToList())
                    .ToList();

                result.BatchCount = batches.Count;

                _logger.LogInformation("Split into {BatchCount} batches of size {BatchSize}", result.BatchCount, batchSize);

                foreach (var batch in batches)
                {
                    if (parallelProcessing)
                    {
                        // Process batch in parallel
                        var parallelOptions = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDegreeOfParallelism
                        };

                        await Parallel.ForEachAsync(batch, parallelOptions, async (item, token) =>
                        {
                            await CreateSingleInvoiceAsync(item.Invoice, item.Lines, result);
                        });
                    }
                    else
                    {
                        // Process batch sequentially
                        foreach (var item in batch)
                        {
                            await CreateSingleInvoiceAsync(item.Invoice, item.Lines, result);
                        }
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.TotalDurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
                result.Success = true;
                result.Message = $"Successfully created {result.SuccessCount} out of {result.TotalItems} invoices";

                _logger.LogInformation("Batch creation completed: {Message}", result.Message);
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

        /// <summary>
        /// Processes a single invoice
        /// </summary>
        /// <param name="invoiceReference"> The invoice reference </param>
        /// <param name="result">           The batch processing result </param>
        private async Task ProcessSingleInvoiceAsync(string invoiceReference, BatchProcessingResult result)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString();

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["InvoiceReference"] = invoiceReference,
                    ["CorrelationId"] = correlationId
                }))
                {
                    _logger.LogInformation("Processing invoice {InvoiceReference}", invoiceReference);

                    var statusResult = await _invoiceService.CheckInvoiceStatusAsync(invoiceReference);

                    if (statusResult.Success)
                    {
                        _logger.LogInformation("Successfully processed invoice {InvoiceReference}, Status: {Status}, OutstandingValue: {OutstandingValue}",
                            invoiceReference, statusResult.IsPaid ? "Paid" : "Not Paid", statusResult.OutstandingValue);

                        // Log status history
                        var invoice = await _invoiceRepository.GetByReferenceAsync(invoiceReference);
                        if (invoice != null)
                        {
                            var statusHistory = new InvoiceStatusHistory
                            {
                                InvoiceReference = invoiceReference,
                                GrossValue = invoice.GrossValue,
                                OutstandingValue = statusResult.OutstandingValue,
                                AllocatedValue = statusResult.AllocatedValue,
                                Status = statusResult.IsPaid ? "Paid" : (statusResult.OutstandingValue < invoice.GrossValue ? "PartiallyPaid" : "Unpaid"),
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
                        _logger.LogWarning("Failed to process invoice {InvoiceReference}: {Message}", invoiceReference, statusResult.Message);

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

        /// <summary>
        /// Creates a single invoice
        /// </summary>
        /// <param name="invoice"> The invoice to create </param>
        /// <param name="lines">   The invoice lines </param>
        /// <param name="result">  The batch processing result </param>
        private async Task CreateSingleInvoiceAsync(Invoice invoice, List<Models.OrderLine> lines, BatchProcessingResult result)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString();

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["InvoiceReference"] = invoice.InvoiceReference,
                    ["CustomerId"] = invoice.CustomerId,
                    ["CorrelationId"] = correlationId
                }))
                {
                    _logger.LogInformation("Creating invoice {InvoiceReference} for customer {CustomerId}", invoice.InvoiceReference, invoice.CustomerId);

                    var createResult = await _invoiceService.CreateSalesOrderInvoiceAsync(invoice, lines);

                    if (createResult.Success)
                    {
                        _logger.LogInformation("Successfully created invoice {InvoiceReference}, OrderId: {OrderId}, OrderReference: {OrderReference}",
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
                        _logger.LogWarning("Failed to create invoice {InvoiceReference}: {Message}", invoice.InvoiceReference, createResult.Message);

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