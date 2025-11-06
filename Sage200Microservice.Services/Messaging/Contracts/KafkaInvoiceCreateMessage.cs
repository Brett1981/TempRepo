using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization; // Ensure this using directive is present

namespace Sage200Microservice.Services.Messaging.Contracts
{
    /// <summary>
    /// Represents an invoice creation request consumed from Kafka.
    /// Mirrors SalesInvoiceCreate but uses string? for dates to handle ISO 8601 format common in Kafka.
    /// Assumes snake_case from upstream producer via JsonPropertyName attributes if needed,
    /// otherwise relies on case-insensitive deserialization.
    /// </summary>
    public sealed class KafkaInvoiceCreateMessage
    {
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }

        [JsonPropertyName("transaction_date")]
        public string? TransactionDate { get; set; } // ISO 8601 string format expected

        [JsonPropertyName("due_date")]
        public string? DueDate { get; set; } // ISO 8601 string format expected

        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; }

        [JsonPropertyName("settled_immediately")]
        public bool? SettledImmediately { get; set; }

        [JsonPropertyName("document_goods_value")]
        public decimal DocumentGoodsValue { get; set; }

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

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("second_reference")]
        public string? SecondReference { get; set; }

        [JsonPropertyName("tax_analysis_items")]
        public List<TaxAnalysisItem>? TaxAnalysisItems { get; set; }

        [JsonPropertyName("nominal_analysis_items")]
        public List<NominalAnalysisItem>? NominalAnalysisItems { get; set; }

        [JsonPropertyName("externalRefs")]
        public List<ExternalRefItem>? ExternalRefs { get; set; }

        // --- Nested classes mirror SalesInvoiceCreate ---
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
    }
}