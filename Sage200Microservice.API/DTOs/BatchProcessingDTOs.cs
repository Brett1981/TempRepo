namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for batch processing request
    /// </summary>
    public class BatchProcessingRequestDto : BatchProcessingOptionsDto
    {
        /// <summary>
        /// The invoice references to process
        /// </summary>
        /// <example> ["INV-2023-001", "INV-2023-002", "INV-2023-003"] </example>
        public List<string> InvoiceReferences { get; set; }
    }

    /// <summary>
    /// DTO for batch processing options
    /// </summary>
    public class BatchProcessingOptionsDto
    {
        /// <summary>
        /// The batch size
        /// </summary>
        /// <example> 100 </example>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Whether to process in parallel
        /// </summary>
        /// <example> true </example>
        public bool ParallelProcessing { get; set; } = true;

        /// <summary>
        /// The maximum degree of parallelism
        /// </summary>
        /// <example> 5 </example>
        public int MaxDegreeOfParallelism { get; set; } = 5;
    }

    /// <summary>
    /// DTO for batch processing response
    /// </summary>
    public class BatchProcessingResponseDto
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
        public List<BatchProcessingItemResponseDto> ProcessedItems { get; set; } = new List<BatchProcessingItemResponseDto>();
    }

    /// <summary>
    /// DTO for batch processing item response
    /// </summary>
    public class BatchProcessingItemResponseDto
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