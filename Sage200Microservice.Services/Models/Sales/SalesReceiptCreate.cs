// =========================================================================================================
// SalesReceiptCreate.cs — COMPLETE MODEL (OpenAPI-aligned, write-only fields, omit-nulls when serialized)
// .NET 9+, ready for Kafka hooks, Docker-friendly.
// =========================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sales
{
    /// <summary>
    /// Create a Sales Receipt (returns URN on success).
    /// </summary>
    public sealed class SalesReceiptCreate
    {
        // ---------------- Required ----------------

        /// <summary>Customer record Id.</summary>
        [Required, Range(1, long.MaxValue)]
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }

        /// <summary>Bank record Id.</summary>
        [Required, Range(1, long.MaxValue)]
        [JsonPropertyName("bank_id")]
        public long BankId { get; set; }

        /// <summary>Value of the receipt.</summary>
        [Required, Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
        [JsonPropertyName("cheque_value")]
        public decimal ChequeValue { get; set; }

        // ---------------- Optionals ----------------

        /// <summary>Receipt currency record Id. Defaults to customer account currency.</summary>
        [JsonPropertyName("cheque_currency_id")]
        public long? ChequeCurrencyId { get; set; }

        /// <summary>Value to post to the customer account.</summary>
        [JsonPropertyName("customer_cheque_value")]
        public decimal? CustomerChequeValue { get; set; }

        /// <summary>Transaction date (defaults to system date). Serialized as UTC.</summary>
        [JsonPropertyName("transaction_date")]
        [Required]
        public DateTimeOffset? TransactionDate { get; set; }

        /// <summary>Exchange rate for the receipt (customer exchange rate). 6dp per API; decimal is fine.</summary>
        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; }

        /// <summary>Exchange rate for the customer to the bank currency.</summary>
        [JsonPropertyName("bank_exchange_rate")]
        public decimal? BankExchangeRate { get; set; }

        /// <summary>Exchange rate for the customer to the cheque currency.</summary>
        [JsonPropertyName("cheque_exchange_rate")]
        public decimal? ChequeExchangeRate { get; set; }

        /// <summary>Settlement discount value.</summary>
        [JsonPropertyName("settlement_discount_value")]
        public decimal? SettlementDiscountValue { get; set; }

        /// <summary>Receipt reference.</summary>
        [MaxLength(40)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        /// <summary>Receipt second reference.</summary>
        [MaxLength(40)]
        [JsonPropertyName("second_reference")]
        public string? SecondReference { get; set; }

        /// <summary>Nominal analysis lines.</summary>
        [JsonPropertyName("nominal_analysis_items")]
        public List<NominalAnalysisItem>? NominalAnalysisItems { get; set; }

        /// <summary>Optional external references tracked by the microservice (not sent to Sage).</summary>
        public List<ExternalRefItem>? ExternalRefs { get; set; }

        // ---------------- Nested types ----------------

        /// <summary>Nominal analysis line.</summary>
        public sealed class NominalAnalysisItem
        {
            /// <summary>Nominal account code (required).</summary>
            [Required, MaxLength(8)]
            [JsonPropertyName("code")]
            public string Code { get; set; } = string.Empty;

            /// <summary>Cost centre (optional; must correspond to code).</summary>
            [MaxLength(3)]
            [JsonPropertyName("cost_centre")]
            public string? CostCentre { get; set; }

            /// <summary>Department (optional; must correspond to code).</summary>
            [MaxLength(3)]
            [JsonPropertyName("department")]
            public string? Department { get; set; }

            /// <summary>Narrative.</summary>
            [MaxLength(6000)]
            [JsonPropertyName("narrative")]
            public string? Narrative { get; set; }

            /// <summary>Value to post to the nominal account.</summary>
            [JsonPropertyName("value")]
            public decimal? Value { get; set; }

            /// <summary>Transaction analysis code (if enabled in ledger settings).</summary>
            [MaxLength(20)]
            [JsonPropertyName("transaction_analysis_code")]
            public string? TransactionAnalysisCode { get; set; }
        }

        /// <summary>External ref mapping (microservice only; not posted to Sage).</summary>
        public sealed class ExternalRefItem
        {
            public int? AppId { get; set; }

            [Required, MaxLength(200)]
            public string ExternalRef { get; set; } = string.Empty;
        }
    }
}
