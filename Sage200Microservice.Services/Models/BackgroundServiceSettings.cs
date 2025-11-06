namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Configuration settings for background services
    /// </summary>
    public class BackgroundServiceSettings
    {
        /// <summary>
        /// Settings for the invoice status background service
        /// </summary>
        public InvoiceStatusServiceSettings InvoiceStatus { get; set; } = new InvoiceStatusServiceSettings();
    }

    /// <summary>
    /// Configuration settings for the invoice status background service
    /// </summary>
    public class InvoiceStatusServiceSettings
    {
        /// <summary>
        /// The interval in minutes between invoice status checks
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>
        /// The maximum number of invoices to process in a single batch
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// The maximum number of retry attempts for failed checks
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// The delay in minutes between retry attempts
        /// </summary>
        public int RetryDelayMinutes { get; set; } = 5;

        /// <summary>
        /// Whether to process invoices in parallel
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;

        /// <summary>
        /// The maximum degree of parallelism for parallel processing
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = 5;

        /// <summary>
        /// Whether the service is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}