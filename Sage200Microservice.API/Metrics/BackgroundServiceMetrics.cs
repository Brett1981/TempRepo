using Prometheus;

namespace Sage200Microservice.API.Metrics
{
    /// <summary>
    /// Metrics for background services
    /// </summary>
    public class BackgroundServiceMetrics
    {
        /// <summary>
        /// Counter for background service executions
        /// </summary>
        public readonly Counter BackgroundServiceExecutions;

        /// <summary>
        /// Counter for background service errors
        /// </summary>
        public readonly Counter BackgroundServiceErrors;

        /// <summary>
        /// Histogram for background service execution duration
        /// </summary>
        public readonly Histogram BackgroundServiceExecutionDuration;

        /// <summary>
        /// Counter for items processed by background services
        /// </summary>
        public readonly Counter ItemsProcessed;

        /// <summary>
        /// Counter for items that failed processing
        /// </summary>
        public readonly Counter ItemsFailedProcessing;

        /// <summary>
        /// Gauge for batch processing queue size
        /// </summary>
        public readonly Gauge BatchProcessingQueueSize;

        /// <summary>
        /// Initializes a new instance of the BackgroundServiceMetrics class
        /// </summary>
        public BackgroundServiceMetrics()
        {
            // Create metrics with appropriate labels
            BackgroundServiceExecutions = Prometheus.Metrics.CreateCounter(
                "sage200_background_service_executions_total",
                "Total number of background service executions",
                new CounterConfiguration
                {
                    LabelNames = new[] { "service", "result" }
                });

            BackgroundServiceErrors = Prometheus.Metrics.CreateCounter(
                "sage200_background_service_errors_total",
                "Total number of background service errors",
                new CounterConfiguration
                {
                    LabelNames = new[] { "service", "error_type" }
                });

            BackgroundServiceExecutionDuration = Prometheus.Metrics.CreateHistogram(
                "sage200_background_service_execution_duration_seconds",
                "Background service execution duration in seconds",
                new HistogramConfiguration
                {
                    LabelNames = new[] { "service" },
                    Buckets = Histogram.ExponentialBuckets(0.1, 2, 10) // 100ms to ~100s
                });

            ItemsProcessed = Prometheus.Metrics.CreateCounter(
                "sage200_background_service_items_processed_total",
                "Total number of items processed by background services",
                new CounterConfiguration
                {
                    LabelNames = new[] { "service", "item_type", "result" }
                });

            ItemsFailedProcessing = Prometheus.Metrics.CreateCounter(
                "sage200_background_service_items_failed_total",
                "Total number of items that failed processing",
                new CounterConfiguration
                {
                    LabelNames = new[] { "service", "item_type", "reason" }
                });

            BatchProcessingQueueSize = Prometheus.Metrics.CreateGauge(
                "sage200_batch_processing_queue_size",
                "Number of items in the batch processing queue",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "service", "item_type" }
                });
        }

        /// <summary>
        /// Records a background service execution
        /// </summary>
        /// <param name="service"> The service name </param>
        /// <param name="result">  The execution result </param>
        public void RecordBackgroundServiceExecution(string service, string result)
        {
            BackgroundServiceExecutions.WithLabels(service, result).Inc();
        }

        /// <summary>
        /// Records a background service error
        /// </summary>
        /// <param name="service">   The service name </param>
        /// <param name="errorType"> The error type </param>
        public void RecordBackgroundServiceError(string service, string errorType)
        {
            BackgroundServiceErrors.WithLabels(service, errorType).Inc();
        }

        /// <summary>
        /// Records a background service execution duration
        /// </summary>
        /// <param name="service">  The service name </param>
        /// <param name="duration"> The duration in seconds </param>
        public void RecordBackgroundServiceExecutionDuration(string service, double duration)
        {
            BackgroundServiceExecutionDuration.WithLabels(service).Observe(duration);
        }

        /// <summary>
        /// Tracks a background service execution
        /// </summary>
        /// <param name="service"> The service name </param>
        /// <returns> A timer that records the duration when disposed </returns>
        public IDisposable TrackBackgroundServiceExecution(string service)
        {
            return BackgroundServiceExecutionDuration.WithLabels(service).NewTimer();
        }

        /// <summary>
        /// Records an item processed by a background service
        /// </summary>
        /// <param name="service">  The service name </param>
        /// <param name="itemType"> The item type </param>
        /// <param name="result">   The processing result </param>
        public void RecordItemProcessed(string service, string itemType, string result)
        {
            ItemsProcessed.WithLabels(service, itemType, result).Inc();
        }

        /// <summary>
        /// Records an item that failed processing
        /// </summary>
        /// <param name="service">  The service name </param>
        /// <param name="itemType"> The item type </param>
        /// <param name="reason">   The failure reason </param>
        public void RecordItemFailedProcessing(string service, string itemType, string reason)
        {
            ItemsFailedProcessing.WithLabels(service, itemType, reason).Inc();
        }

        /// <summary>
        /// Sets the batch processing queue size
        /// </summary>
        /// <param name="service">  The service name </param>
        /// <param name="itemType"> The item type </param>
        /// <param name="size">     The queue size </param>
        public void SetBatchProcessingQueueSize(string service, string itemType, int size)
        {
            BatchProcessingQueueSize.WithLabels(service, itemType).Set(size);
        }
    }
}