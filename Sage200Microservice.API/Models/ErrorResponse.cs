namespace Sage200Microservice.API.Models
{
    /// <summary>
    /// Response model for errors
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// The HTTP status code
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The error message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The correlation ID for tracking
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// The timestamp of the error
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Validation errors by field
        /// </summary>
        public Dictionary<string, string[]> Errors { get; set; }

        public string Details { get; set; }
    }
}