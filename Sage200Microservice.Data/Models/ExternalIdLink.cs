using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Data.Models
{
    /// <summary>
    /// Cross-application external reference → Sage identifier mapping (source of truth).
    /// </summary>
    public class ExternalIdLink
    {
        /// <summary>
        /// Surrogate primary key.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Calling application identifier (resolved from API Key).
        /// </summary>
        public int AppId { get; set; }

        /// <summary>
        /// Entity type being linked (persisted as NVARCHAR(40)).
        /// </summary>
        public ExternalEntityType EntityType { get; set; }

        /// <summary>
        /// Numeric Sage identifier where exposed (e.g., Customers, SOP Orders).
        /// </summary>
        public long? SageId { get; set; }

        /// <summary>
        /// URN-style Sage identifier where numeric IDs are not exposed (e.g., receipts/payments).
        /// </summary>
        [MaxLength(128)]
        public string? SageUrn { get; set; }

        /// <summary>
        /// External reference from the calling app (e.g., BRE001).
        /// </summary>
        [MaxLength(200)]
        public string ExternalRef { get; set; } = default!;

        /// <summary>
        /// UTC timestamp when this link was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        // Allocation tracking fields — used when EntityType == ExternalEntityType.SalesInvoice

        /// <summary>
        /// Indicates whether the linked SalesInvoice has been fully allocated (outstanding == 0).
        /// </summary>
        public bool IsFullyAllocated { get; set; } = false;

        /// <summary>
        /// Total amount allocated against the invoice so far (Sage value).
        /// </summary>
        [Precision(18, 2)]
        public decimal? AllocatedValue { get; set; }

        /// <summary>
        /// Outstanding value remaining to be allocated (Sage value).
        /// </summary>
        [Precision(18, 2)]
        public decimal? OutstandingValue { get; set; }

        /// <summary>
        /// UTC timestamp when allocation status was last checked.
        /// </summary>
        public DateTime? LastAllocationCheckUtc { get; set; }

        /// <summary>
        /// UTC timestamp when allocation status last changed (e.g., newly paid in full).
        /// </summary>
        public DateTime? LastAllocationChangeUtc { get; set; }
    }
}