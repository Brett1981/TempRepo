using Sage200Microservice.Data.Models;
using System.Threading;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Interface for batch processing service
    /// </summary>
    public interface IBatchProcessingService
    {
        /// <summary>
        /// Processes a batch of invoices.
        /// </summary>
        Task<BatchProcessingResult> ProcessInvoiceBatchAsync(
            IEnumerable<string> invoiceReferences,
            int batchSize = 100,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a batch of invoices.
        /// </summary>
        Task<BatchProcessingResult> CreateInvoiceBatchAsync(
            IEnumerable<(Invoice Invoice, List<Models.OrderLine> Lines)> invoices,
            int batchSize = 50,
            bool parallelProcessing = true,
            int maxDegreeOfParallelism = 5,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Result of a batch processing operation
    /// </summary>
    public class BatchProcessingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
        public int TotalItems { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int BatchCount { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double TotalDurationMs { get; set; }
        public List<BatchProcessingItem> ProcessedItems { get; set; } = new();
    }

    public class BatchProcessingItem
    {
        public string ItemId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
    }
}
