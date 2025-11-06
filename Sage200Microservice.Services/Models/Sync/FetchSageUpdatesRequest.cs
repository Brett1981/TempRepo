using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Services.Models.Sync
{
    /// <summary>
    /// Request body for triggering a background synchronization process.
    /// </summary>
    public class FetchSageUpdatesRequest
    {
        /// <summary>
        /// Specifies the type of entity to synchronize.
        /// Examples: "Payments", "Allocations", "Customers", "Invoices", "All"
        /// </summary>
        [Required(ErrorMessage = "EntityType is required.")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "EntityType must be between 1 and 50 characters.")]
        public string EntityType { get; set; } = default!;

        /// <summary>
        /// If true, forces a full synchronization, potentially ignoring other parameters like SyncFrom.
        /// Defaults to false.
        /// </summary>
        public bool ForceFullSync { get; set; } = false;

        // Note: Add other relevant parameters here as needed in future, e.g.,
        // public DateTime? SyncFrom { get; set; }
    }
}