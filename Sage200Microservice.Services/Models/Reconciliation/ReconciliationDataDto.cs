using System;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Services.Models.Reconciliation
{
    /// <summary>
    /// Represents a piece of reconciled data, linking an external reference to fetched Sage transaction details.
    /// NOTE: This is a placeholder structure for Stage 8. Fields from SagePayments/SageAllocations
    /// will be finalized in Stage 10 based on OpenAPI definitions.
    /// </summary>
    public class ReconciliationDataDto
    {
        // --- From ExternalIdLink ---

        /// <summary>
        /// The AppId of the application that owns the external reference.
        /// </summary>
        public int AppId { get; set; }

        /// <summary>
        /// The external reference string used by the calling application.
        /// </summary>
        public string ExternalRef { get; set; } = default!;

        /// <summary>
        /// The type of Sage entity linked (e.g., "SalesPayment", "SalesInvoice").
        /// Stored as a string based on the ExternalEntityType enum.
        /// </summary>
        public string EntityType { get; set; } = default!;

        // --- From Sage (One of these should ideally be populated) ---

        /// <summary>
        /// The numeric Sage ID, if applicable (e.g., for Customers, SOP Orders).
        /// Null for entities identified primarily by URN.
        /// </summary>
        public long? SageId { get; set; }

        /// <summary>
        /// The URN (Unique Reference Number) identifier from Sage, if applicable
        /// (e.g., for Sales Payments, Allocations, Invoices, Credit Notes).
        /// Null for entities identified primarily by numeric ID.
        /// </summary>
        public string? SageTransactionUrn { get; set; }

        // --- From Sage Transaction Data (SagePayments / SageAllocations - PLACEHOLDERS) ---
        // These fields need to be confirmed/refined based on actual Sage model definitions in Stage 10

        /// <summary>
        /// The date of the transaction in Sage (Placeholder).
        /// </summary>
        public DateTime? TransactionDate { get; set; }

        /// <summary>
        /// The value/amount of the transaction (Placeholder).
        /// </summary>
        public decimal? Amount { get; set; }

        /// <summary>
        /// The currency code of the transaction (Placeholder).
        /// </summary>
        public string? CurrencyCode { get; set; } // e.g., "GBP"

        /// <summary>
        /// A reference associated with the transaction in Sage (e.g., Payment Reference) (Placeholder).
        /// </summary>
        public string? SageReference { get; set; }

        /// <summary>
        /// The Sage customer account reference linked to the transaction (Placeholder).
        /// </summary>
        public string? SageAccountReference { get; set; }

        /// <summary>
        /// Additional details, potentially related to allocations (Placeholder).
        /// Could be a list or complex object later.
        /// </summary>
        public string? AllocationDetails { get; set; } // Example placeholder

        /// <summary>
        /// The date/time this specific linked record was created in our database.
        /// </summary>
        public DateTime LinkCreatedAtUtc { get; set; }
    }
}