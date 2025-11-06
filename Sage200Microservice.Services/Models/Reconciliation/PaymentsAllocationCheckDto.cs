using System;

namespace Sage200Microservice.Services.Models.Reconciliation
{
    /// <summary>
    /// DTO representing the result of an allocation refresh check for a single invoice.
    /// </summary>
    public sealed class PaymentsAllocationCheckDto
    {
        /// <summary>Calling application identifier (from ApiKeys.Id).</summary>
        public int AppId { get; set; }

        /// <summary>The external application's unique identifier for this record.</summary>
        public string ExternalRef { get; set; } = string.Empty;

        /// <summary>The URN returned from Sage for the SalesInvoice.</summary>
        public string? SageUrn { get; set; }

        /// <summary>The total value allocated in Sage for this invoice.</summary>
        public decimal? AllocatedValue { get; set; }

        /// <summary>The remaining outstanding value in Sage for this invoice.</summary>
        public decimal? OutstandingValue { get; set; }

        /// <summary>Last time allocations were checked for this invoice (UTC).</summary>
        public DateTime? LastAllocationCheckUtc { get; set; }

        /// <summary>When allocation values were last observed to change (UTC).</summary>
        public DateTime? LastAllocationChangeUtc { get; set; }

        /// <summary>True if allocation values changed compared to the database record.</summary>
        public bool Changed { get; set; }

        /// <summary>Status information or error text for this item; null when successful and unchanged.</summary>
        public string? StatusMessage { get; set; }
    }
}
