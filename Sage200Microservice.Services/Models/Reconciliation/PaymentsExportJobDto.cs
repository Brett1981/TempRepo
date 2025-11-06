using System;

namespace Sage200Microservice.Services.Models.Reconciliation
{
    /// <summary>
    /// DTO representing an allocation candidate for the Airflow export job (Spec §6.3.1).
    /// Contains the minimal data required for identifying which Sage invoices need allocation checks.
    /// </summary>
    public sealed class PaymentsExportJobDto
    {
        /// <summary>Calling application identifier (from ApiKeys.Id).</summary>
        public int AppId { get; set; }

        /// <summary>The external application's unique identifier for this record.</summary>
        public string ExternalRef { get; set; } = string.Empty;

        /// <summary>The URN returned from Sage for the SalesInvoice.</summary>
        public string? SageUrn { get; set; }

        /// <summary>The total value allocated so far (if known).</summary>
        public decimal? AllocatedValue { get; set; }

        /// <summary>The remaining outstanding value of this invoice in Sage.</summary>
        public decimal? OutstandingValue { get; set; }

        /// <summary>Last time we checked allocations for this invoice (UTC).</summary>
        public DateTime? LastAllocationCheckUtc { get; set; }

        /// <summary>Last time an allocation value was observed to change (UTC).</summary>
        public DateTime? LastAllocationChangeUtc { get; set; }
    }
}