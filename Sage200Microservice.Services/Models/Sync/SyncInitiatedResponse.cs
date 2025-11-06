using System;

namespace Sage200Microservice.Services.Models.Sync
{
    /// <summary>
    /// Response returned when a synchronization request is accepted.
    /// </summary>
    public class SyncInitiatedResponse
    {
        /// <summary>
        /// The unique correlation ID assigned to this synchronization job attempt.
        /// Use this ID to track the job's status later (e.g., via a future monitoring endpoint).
        /// </summary>
        public Guid CorrelationId { get; set; }

        /// <summary>
        /// Indicates the initial status of the request.
        /// </summary>
        public string Status { get; set; } = "Synchronization request queued.";

        /// <summary>
        /// The UTC timestamp when the request was accepted.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
