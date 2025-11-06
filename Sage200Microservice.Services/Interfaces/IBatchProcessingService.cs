using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Interface for batch processing service
    /// </summary>
    public interface IBatchProcessingService
    {
        /// <summary>
        /// Processes a batch of invoices
        /// </summary>
        /// <param name="invoiceReferences">      The invoice references to process </param>
        /// <param name="batchSize">              The batch size </param>
        /// <param name="parallelProcessing">     Whether to process in parallel </param>
        /// <param name="maxDegreeOfParallelism"> The maximum degree of parallelism </param>
        /// <returns> The batch processing result </returns>
        Task<BatchProcessingResult> ProcessInvoiceBatchAsync(
            IEnumerable<string> invoiceReferences,
            int batchSize = 100,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5);

        /// <summary>
        /// Creates a batch of invoices
        /// </summary>
        /// <param name="invoices">               The invoices to create </param>
        /// <param name="batchSize">              The batch size </param>
        /// <param name="parallelProcessing">     Whether to process in parallel </param>
        /// <param name="maxDegreeOfParallelism"> The maximum degree of parallelism </param>
        /// <returns> The batch processing result </returns>
        Task<BatchProcessingResult> CreateInvoiceBatchAsync(
            IEnumerable<(Invoice Invoice, List<Models.OrderLine> Lines)> invoices,
            int batchSize = 50,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5);
    }

    /// <summary>
    /// Result of a batch processing operation
    /// </summary>
    public class BatchProcessingResult
    {
        /// <summary>
        /// Whether the batch processing was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The error details if the operation failed
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// The total number of items in the batch
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// The number of items that were processed successfully
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// The number of items that failed to process
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// The number of batches
        /// </summary>
        public int BatchCount { get; set; }

        /// <summary>
        /// The start time of the batch processing
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// The end time of the batch processing
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// The total duration of the batch processing in milliseconds
        /// </summary>
        public double TotalDurationMs { get; set; }

        /// <summary>
        /// The list of processed items
        /// </summary>
        public List<BatchProcessingItem> ProcessedItems { get; set; } = new List<BatchProcessingItem>();
    }

    /// <summary>
    /// An item in a batch processing operation
    /// </summary>
    public class BatchProcessingItem
    {
        /// <summary>
        /// The ID of the item
        /// </summary>
        public string ItemId { get; set; }

        /// <summary>
        /// Whether the item was processed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The error details if the operation failed
        /// </summary>
        public string ErrorDetails { get; set; }
    }
}