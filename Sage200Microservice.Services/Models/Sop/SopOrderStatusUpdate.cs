namespace Sage200Microservice.Services.Models.Sop
{
    /// <summary>
    /// Request to amend a SOP Order's document status.
    /// </summary>
    public sealed class SopOrderStatusUpdate
    {
        /// <summary>64-bit Sage SOP order id (ID-first policy).</summary>
        public long OrderId { get; set; }

        /// <summary>
        /// Target status (friendly) - one of: Live, OnHold, Cancelled, Completed.
        /// Will be mapped to Sage enum literal before calling Sage.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Optional reason/comment to include with the status change (if Sage supports it in your build).
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Result of a status update request.
    /// </summary>
    public sealed class SopOrderStatusUpdateResult
    {
        /// <summary>True if Sage accepted and applied the status change.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable message (from Sage or mapping layer).</summary>
        public string? Message { get; set; }

        /// <summary>Echo of order id.</summary>
        public long OrderId { get; set; }

        /// <summary>
        /// New status (friendly short form) as confirmed by Sage response, if available.
        /// </summary>
        public string? NewStatus { get; set; }
    }
}
