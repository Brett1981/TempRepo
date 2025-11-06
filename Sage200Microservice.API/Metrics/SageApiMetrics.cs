using Prometheus;

namespace Sage200Microservice.API.Metrics
{
    /// <summary>
    /// Metrics for Sage API operations
    /// </summary>
    public class SageApiMetrics
    {
        /// <summary>
        /// Counter for Sage API requests
        /// </summary>
        public readonly Counter SageApiRequests;

        /// <summary>
        /// Counter for Sage API errors
        /// </summary>
        public readonly Counter SageApiErrors;

        /// <summary>
        /// Histogram for Sage API request duration
        /// </summary>
        public readonly Histogram SageApiRequestDuration;

        /// <summary>
        /// Counter for authentication token refreshes
        /// </summary>
        public readonly Counter TokenRefreshes;

        /// <summary>
        /// Counter for authentication failures
        /// </summary>
        public readonly Counter AuthenticationFailures;

        /// <summary>
        /// Initializes a new instance of the SageApiMetrics class
        /// </summary>
        public SageApiMetrics()
        {
            // Create metrics with appropriate labels
            SageApiRequests = Prometheus.Metrics.CreateCounter(
                "sage200_sage_api_requests_total",
                "Total number of Sage API requests",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "status_code" }
                });

            SageApiErrors = Prometheus.Metrics.CreateCounter(
                "sage200_sage_api_errors_total",
                "Total number of Sage API errors",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "error_type" }
                });

            SageApiRequestDuration = Prometheus.Metrics.CreateHistogram(
                "sage200_sage_api_request_duration_seconds",
                "Sage API request duration in seconds",
                new HistogramConfiguration
                {
                    LabelNames = new[] { "method", "endpoint" },
                    Buckets = Histogram.ExponentialBuckets(0.1, 2, 10) // 100ms to ~100s
                });

            TokenRefreshes = Prometheus.Metrics.CreateCounter(
                "sage200_sage_api_token_refreshes_total",
                "Total number of authentication token refreshes",
                new CounterConfiguration
                {
                    LabelNames = new[] { "result" }
                });

            AuthenticationFailures = Prometheus.Metrics.CreateCounter(
                "sage200_sage_api_authentication_failures_total",
                "Total number of authentication failures",
                new CounterConfiguration
                {
                    LabelNames = new[] { "reason" }
                });
        }

        /// <summary>
        /// Records a Sage API request
        /// </summary>
        /// <param name="method">     The HTTP method </param>
        /// <param name="endpoint">   The API endpoint </param>
        /// <param name="statusCode"> The HTTP status code </param>
        public void RecordSageApiRequest(string method, string endpoint, int statusCode)
        {
            SageApiRequests.WithLabels(method, endpoint, statusCode.ToString()).Inc();
        }

        /// <summary>
        /// Records a Sage API error
        /// </summary>
        /// <param name="method">    The HTTP method </param>
        /// <param name="endpoint">  The API endpoint </param>
        /// <param name="errorType"> The error type </param>
        public void RecordSageApiError(string method, string endpoint, string errorType)
        {
            SageApiErrors.WithLabels(method, endpoint, errorType).Inc();
        }

        /// <summary>
        /// Records a Sage API request duration
        /// </summary>
        /// <param name="method">   The HTTP method </param>
        /// <param name="endpoint"> The API endpoint </param>
        /// <param name="duration"> The duration in seconds </param>
        public void RecordSageApiRequestDuration(string method, string endpoint, double duration)
        {
            SageApiRequestDuration.WithLabels(method, endpoint).Observe(duration);
        }

        /// <summary>
        /// Tracks a Sage API request
        /// </summary>
        /// <param name="method">   The HTTP method </param>
        /// <param name="endpoint"> The API endpoint </param>
        /// <returns> A timer that records the duration when disposed </returns>
        public IDisposable TrackSageApiRequest(string method, string endpoint)
        {
            return SageApiRequestDuration.WithLabels(method, endpoint).NewTimer();
        }

        /// <summary>
        /// Records a token refresh
        /// </summary>
        /// <param name="result"> The result of the refresh (success or failure) </param>
        public void RecordTokenRefresh(string result)
        {
            TokenRefreshes.WithLabels(result).Inc();
        }

        /// <summary>
        /// Records an authentication failure
        /// </summary>
        /// <param name="reason"> The reason for the failure </param>
        public void RecordAuthenticationFailure(string reason)
        {
            AuthenticationFailures.WithLabels(reason).Inc();
        }
    }
}