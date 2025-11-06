using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sales
{
    /// <summary>
    /// Create a Sales Invoice (URN-based).
    /// </summary>
    public sealed class SalesInvoiceCreate
    {
        [Required]
        [Range(1, long.MaxValue)]
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }

        [JsonPropertyName("transaction_date")]
        [Required]
        public DateTimeOffset? TransactionDate { get; set; }

        [JsonPropertyName("due_date")]
        public DateTimeOffset? DueDate { get; set; }

        /// <summary>
        /// Optional exchange rate (decimal 6dp).
        /// </summary>
        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; }

        /// <summary>
        /// Mark as settled immediately.
        /// </summary>
        [JsonPropertyName("settled_immediately")]
        public bool? SettledImmediately { get; set; }

        // Ledger header totals are REQUIRED by Helper Files
        [Required]
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        [JsonPropertyName("document_goods_value")]
        public decimal DocumentGoodsValue { get; set; }

        [Required]
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        [JsonPropertyName("document_tax_value")]
        public decimal DocumentTaxValue { get; set; }

        [JsonPropertyName("document_discount_value")]
        public decimal? DocumentDiscountValue { get; set; }

        [JsonPropertyName("document_tax_discount_value")]
        public decimal? DocumentTaxDiscountValue { get; set; }

        [JsonPropertyName("discount_percent")]
        public decimal? DiscountPercent { get; set; }

        [JsonPropertyName("discount_days")]
        public short? DiscountDays { get; set; }

        [JsonPropertyName("triangular_transaction")]
        public bool? TriangularTransaction { get; set; }

        // Optional convenience mirror fields (non-authoritative)
        [MaxLength(40)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [MaxLength(40)]
        [JsonPropertyName("second_reference")]
        public string? SecondReference { get; set; }

        /// <summary>
        /// Tax analysis lines.
        /// </summary>
        [JsonPropertyName("tax_analysis_items")]
        public List<TaxAnalysisItem>? TaxAnalysisItems { get; set; }

        /// <summary>
        /// Nominal analysis lines.
        /// </summary>
        [JsonPropertyName("nominal_analysis_items")]
        public List<NominalAnalysisItem>? NominalAnalysisItems { get; set; }

        public sealed class TaxAnalysisItem
        {
            /// <summary>
            /// Tax code record Id.
            /// </summary>
            [Required]
            [Range(1, long.MaxValue)]
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("goods_amount")]
            public decimal? GoodsAmount { get; set; }

            [JsonPropertyName("discount_amount")]
            public decimal? DiscountAmount { get; set; }

            [JsonPropertyName("tax_amount")]
            public decimal? TaxAmount { get; set; }

            [JsonPropertyName("tax_discount_amount")]
            public decimal? TaxDiscountAmount { get; set; }
        }

        public sealed class NominalAnalysisItem
        {
            /// <summary>
            /// Nominal account code (max 8).
            /// </summary>
            [Required, MaxLength(8)]
            [JsonPropertyName("code")]
            public string Code { get; set; } = string.Empty;

            [MaxLength(3)]
            [JsonPropertyName("cost_centre")]
            public string? CostCentre { get; set; }

            [MaxLength(3)]
            [JsonPropertyName("department")]
            public string? Department { get; set; }

            [MaxLength(6000)]
            [JsonPropertyName("narrative")]
            public string? Narrative { get; set; }

            [JsonPropertyName("value")]
            public decimal? Value { get; set; }

            [MaxLength(20)]
            [JsonPropertyName("transaction_analysis_code")]
            public string? TransactionAnalysisCode { get; set; }
        }

        public sealed class ExternalRefItem
        {
            public int? AppId { get; set; }
            public string ExternalRef { get; set; } = string.Empty;
        }

        public List<ExternalRefItem>? ExternalRefs { get; set; }
    }
}