using Prometheus;

namespace Sage200Microservice.API.Metrics
{
    /// <summary>
    /// Metrics for API operations
    /// </summary>
    public class ApiMetrics
    {
        /// <summary>
        /// Counter for API requests
        /// </summary>
        public readonly Counter ApiRequests;

        /// <summary>
        /// Counter for API errors
        /// </summary>
        public readonly Counter ApiErrors;

        /// <summary>
        /// Histogram for API request duration
        /// </summary>
        public readonly Histogram ApiRequestDuration;

        /// <summary>
        /// Counter for rate limited requests
        /// </summary>
        public readonly Counter RateLimitedRequests;

        /// <summary>
        /// Gauge for active API requests
        /// </summary>
        public readonly Gauge ActiveApiRequests;

        /// <summary>
        /// Initializes a new instance of the ApiMetrics class
        /// </summary>
        public ApiMetrics()
        {
            // Create metrics with appropriate labels
            ApiRequests = Prometheus.Metrics.CreateCounter(
                "sage200_api_requests_total",
                "Total number of API requests",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "status_code" }
                });

            ApiErrors = Prometheus.Metrics.CreateCounter(
                "sage200_api_errors_total",
                "Total number of API errors",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "error_type" }
                });

            ApiRequestDuration = Prometheus.Metrics.CreateHistogram(
                "sage200_api_request_duration_seconds",
                "API request duration in seconds",
                new HistogramConfiguration
                {
                    LabelNames = new[] { "method", "endpoint" },
                    Buckets = Histogram.ExponentialBuckets(0.01, 2, 10) // 10ms to ~10s
                });

            RateLimitedRequests = Prometheus.Metrics.CreateCounter(
                "sage200_api_rate_limited_requests_total",
                "Total number of rate limited API requests",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "client_id" }
                });

            ActiveApiRequests = Prometheus.Metrics.CreateGauge(
                "sage200_api_active_requests",
                "Number of currently active API requests",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "method", "endpoint" }
                });
        }

        /// <summary>
        /// Records an API request
        /// </summary>
        /// <param name="method">     The HTTP method </param>
        /// <param name="endpoint">   The API endpoint </param>
        /// <param name="statusCode"> The HTTP status code </param>
        public void RecordApiRequest(string method, string endpoint, int statusCode)
        {
            ApiRequests.WithLabels(method, endpoint, statusCode.ToString()).Inc();
        }

        /// <summary>
        /// Records an API error
        /// </summary>
        /// <param name="method">    The HTTP method </param>
        /// <param name="endpoint">  The API endpoint </param>
        /// <param name="errorType"> The error type </param>
        public void RecordApiError(string method, string endpoint, string errorType)
        {
            ApiErrors.WithLabels(method, endpoint, errorType).Inc();
        }

        /// <summary>
        /// Records an API request duration
        /// </summary>
        /// <param name="method">   The HTTP method </param>
        /// <param name="endpoint"> The API endpoint </param>
        /// <param name="duration"> The duration in seconds </param>
        public void RecordApiRequestDuration(string method, string endpoint, double duration)
        {
            ApiRequestDuration.WithLabels(method, endpoint).Observe(duration);
        }

        /// <summary>
        /// Records a rate limited request
        /// </summary>
        /// <param name="method">   The HTTP method </param>
        /// <param name="endpoint"> The API endpoint </param>
        /// <param name="clientId"> The client ID </param>
        public void RecordRateLimitedRequest(string method, string endpoint, string clientId)
        {
            RateLimitedRequests.WithLabels(method, endpoint, clientId).Inc();
        }

        /// <summary>
        /// Tracks an active API request
        /// </summary>
        /// <param name="method">   The HTTP method </param>
        /// <param name="endpoint"> The API endpoint </param>
        /// <returns> A disposable that decrements the gauge when disposed </returns>
        public IDisposable TrackApiRequest(string method, string endpoint)
        {
            return ActiveApiRequests.WithLabels(method, endpoint).TrackInProgress();
        }
    }
}