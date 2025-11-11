using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sage200Microservice.API.Controllers.Infrastructure; // SageRouteControllerBase
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;            // IBatchProcessingService
using Sage200Microservice.Services;                      // ISageApiClient
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Controller for batch operations.
    /// Ensures Sage routing (X-Site / X-Company) is present before invoking services that call Sage.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class BatchController : SageRouteControllerBase
    {
        private readonly IBatchProcessingService _batchProcessingService;
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IApiLogRepository _apiLogRepository;
        private readonly ILogger<BatchController> _logger;

        public BatchController(
            ISageApiClient sage,                                // <-- required by base
            ILogger<BatchController> logger,
            IBatchProcessingService batchProcessingService,
            IInvoiceRepository invoiceRepository,
            IApiLogRepository apiLogRepository)
            : base(sage, logger)                                // <-- IMPORTANT
        {
            _batchProcessingService = batchProcessingService;
            _invoiceRepository = invoiceRepository;
            _apiLogRepository = apiLogRepository;
            _logger = logger;
        }

        /// <summary>
        /// Processes a batch of invoices.
        /// </summary>
        [HttpPost("process-invoices")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(BatchProcessingResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(BatchProcessingResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(BatchProcessingResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<BatchProcessingResponse>> ProcessInvoiceBatch(
            [FromBody] BatchProcessingRequest request,
            CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // sensible cap for batch ops

            // Ensure X-Site/X-Company are available for any downstream Sage calls:
            await EnsureRoutingAsync(cts.Token);

            // Log the API call
            var apiLog = new Sage200Microservice.Data.Models.ApiLog
            {
                Endpoint = "/api/batch/process-invoices",
                RequestMethod = "POST",
                RequestPayload = $"InvoiceReferences: {request.InvoiceReferences?.Count}, BatchSize: {request.BatchSize}, ParallelProcessing: {request.ParallelProcessing}",
                Timestamp = DateTime.UtcNow,
                CallerId = HttpContext.Request.Headers["caller-id"].FirstOrDefault() ?? "Unknown",
                ApiType = "REST"
            };

            try
            {
                // Validate request
                if (request.InvoiceReferences == null || request.InvoiceReferences.Count == 0)
                {
                    apiLog.ResponsePayload = "No invoice references provided";
                    apiLog.HttpStatusCode = 400;
                    await _apiLogRepository.AddAsync(apiLog);

                    return BadRequest(new BatchProcessingResponse
                    {
                        Success = false,
                        Message = "No invoice references provided"
                    });
                }

                // Process the batch (propagate token)
                var result = await _batchProcessingService.ProcessInvoiceBatchAsync(
                    request.InvoiceReferences,
                    request.BatchSize,
                    request.ParallelProcessing,
                    5,
                    cts.Token);

                // Update API log with response
                apiLog.ResponsePayload = $"Success: {result.Success}, Message: {result.Message}, SuccessCount: {result.SuccessCount}, FailureCount: {result.FailureCount}";
                apiLog.HttpStatusCode = result.Success ? 200 : 500;
                await _apiLogRepository.AddAsync(apiLog);

                var response = new BatchProcessingResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    TotalItems = result.TotalItems,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    BatchCount = result.BatchCount,
                    StartTime = result.StartTime,
                    EndTime = result.EndTime,
                    TotalDurationMs = result.TotalDurationMs,
                    ProcessedItems = result.ProcessedItems.Select(i => new BatchProcessingItemResponse
                    {
                        ItemId = i.ItemId,
                        Success = i.Success,
                        Message = i.Message,
                        ErrorDetails = i.ErrorDetails
                    }).ToList()
                };

                return Ok(response);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Batch processing cancelled due to timeout.");
                return StatusCode(504, new BatchProcessingResponse
                {
                    Success = false,
                    Message = "Batch processing timed out."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invoice batch");

                // Update API log with error response
                apiLog.ResponsePayload = ex.Message;
                apiLog.HttpStatusCode = 500;
                await _apiLogRepository.AddAsync(apiLog);

                return StatusCode(500, new BatchProcessingResponse
                {
                    Success = false,
                    Message = $"Error processing invoice batch: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Processes all outstanding invoices.
        /// </summary>
        [HttpPost("process-outstanding-invoices")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(BatchProcessingResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(BatchProcessingResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<BatchProcessingResponse>> ProcessOutstandingInvoices(
            [FromBody] BatchProcessingOptions request,
            CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            // Ensure routing for any downstream Sage calls:
            await EnsureRoutingAsync(cts.Token);

            // Log the API call
            var apiLog = new Sage200Microservice.Data.Models.ApiLog
            {
                Endpoint = "/api/batch/process-outstanding-invoices",
                RequestMethod = "POST",
                RequestPayload = $"BatchSize: {request.BatchSize}, ParallelProcessing: {request.ParallelProcessing}",
                Timestamp = DateTime.UtcNow,
                CallerId = HttpContext.Request.Headers["caller-id"].FirstOrDefault() ?? "Unknown",
                ApiType = "REST"
            };

            try
            {
                // Get all outstanding invoices
                var outstandingInvoices = await _invoiceRepository.GetOutstandingInvoicesAsync(cts.Token);
                var invoiceReferences = outstandingInvoices.Select(i => i.InvoiceReference).ToList();

                _logger.LogInformation("Found {Count} outstanding invoices to process", invoiceReferences.Count);

                // Process the batch (propagate token)
                var result = await _batchProcessingService.ProcessInvoiceBatchAsync(
                    invoiceReferences,
                    request.BatchSize,
                    request.ParallelProcessing,
                    5,
                    cts.Token);

                // Update API log with response
                apiLog.ResponsePayload = $"Success: {result.Success}, Message: {result.Message}, SuccessCount: {result.SuccessCount}, FailureCount: {result.FailureCount}";
                apiLog.HttpStatusCode = result.Success ? 200 : 500;
                await _apiLogRepository.AddAsync(apiLog);

                var response = new BatchProcessingResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    TotalItems = result.TotalItems,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    BatchCount = result.BatchCount,
                    StartTime = result.StartTime,
                    EndTime = result.EndTime,
                    TotalDurationMs = result.TotalDurationMs,
                    ProcessedItems = result.ProcessedItems.Select(i => new BatchProcessingItemResponse
                    {
                        ItemId = i.ItemId,
                        Success = i.Success,
                        Message = i.Message,
                        ErrorDetails = i.ErrorDetails
                    }).ToList()
                };

                return Ok(response);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Outstanding-invoices batch cancelled due to timeout.");
                return StatusCode(504, new BatchProcessingResponse
                {
                    Success = false,
                    Message = "Operation timed out."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outstanding invoices");

                // Update API log with error response
                apiLog.ResponsePayload = ex.Message;
                apiLog.HttpStatusCode = 500;
                await _apiLogRepository.AddAsync(apiLog);

                return StatusCode(500, new BatchProcessingResponse
                {
                    Success = false,
                    Message = $"Error processing outstanding invoices: {ex.Message}"
                });
            }
        }
    }

    // Request/response models unchanged...
}


/// <summary>
/// Request model for batch processing
/// </summary>
public class BatchProcessingRequest : BatchProcessingOptions
    {
        /// <summary>
        /// The list of invoice references to process
        /// </summary>
        /// <example> ["INV001", "INV002", "INV003"] </example>
        public List<string> InvoiceReferences { get; set; }
    }

    /// <summary>
    /// Request model for batch processing options
    /// </summary>
    public class BatchProcessingOptions
    {
        /// <summary>
        /// The batch size
        /// </summary>
        /// <example> 10 </example>
        public int BatchSize { get; set; } = 10;

        /// <summary>
        /// Whether to process batches in parallel
        /// </summary>
        /// <example> true </example>
        public bool ParallelProcessing { get; set; } = true;
    }

    /// <summary>
    /// Response model for batch processing
    /// </summary>
    public class BatchProcessingResponse
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The total number of items processed
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// The number of items processed successfully
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// The number of items that failed processing
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// The number of batches processed
        /// </summary>
        public int BatchCount { get; set; }

        /// <summary>
        /// When the batch processing started
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the batch processing ended
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// The total duration of the batch processing in milliseconds
        /// </summary>
        public double TotalDurationMs { get; set; }

        /// <summary>
        /// The list of processed items
        /// </summary>
        public List<BatchProcessingItemResponse> ProcessedItems { get; set; } = new List<BatchProcessingItemResponse>();
    }

    /// <summary>
    /// Response model for a batch processing item
    /// </summary>
    public class BatchProcessingItemResponse
    {
        /// <summary>
        /// The ID of the item
        /// </summary>
        public string ItemId { get; set; }

        /// <summary>
        /// Indicates whether the item was processed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the item processing
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Details of any error that occurred during processing
        /// </summary>
        public string ErrorDetails { get; set; }
    }
