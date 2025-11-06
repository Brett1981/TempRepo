/* =========================================================================================================
 * SalesCreditNoteCreate.cs  —  COMPLETE MODEL (WRITEABLE FIELDS ONLY)
 *
 * Built from the OpenAPI "sales_credit_note" definition. Read-only members are NOT exposed.
 * All numeric/date fields are nullable so we omit them from JSON when not supplied (no explicit nulls).
 *
 * Key arrays:
 *  - tax_analysis_items: requires id (tax code id) with amounts (goods/tax/discount/tax_discount).
 *  - nominal_analysis_items: requires code (nominal account), optional cost_centre/department/narrative/value/transaction_analysis_code.
 *
 * IMPORTANT: We intentionally do NOT include "due_date" because Sage exposes that on invoices, not credit notes.
 * ========================================================================================================= */

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sales
{
    /// <summary>
    /// Create a Sales Credit Note (URN-based).
    /// </summary>
    public sealed class SalesCreditNoteCreate
    {
        [Required]
        [Range(1, long.MaxValue)]
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }

        [JsonPropertyName("transaction_date")]
        [Required]
        public DateTimeOffset? TransactionDate { get; set; }

        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; }

        [MaxLength(40)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [MaxLength(40)]
        [JsonPropertyName("second_reference")]
        public string? SecondReference { get; set; }

        [JsonPropertyName("settled_immediately")]
        public bool? SettledImmediately { get; set; }

        [JsonPropertyName("document_goods_value")]
        [Required]
        public decimal? DocumentGoodsValue { get; set; }

        [JsonPropertyName("document_tax_value")]
        [Required]
        public decimal? DocumentTaxValue { get; set; }

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

        // ---------- Analysis Collections ----------

        [JsonPropertyName("tax_analysis_items")]
        public List<TaxAnalysisItem>? TaxAnalysisItems { get; set; }

        [JsonPropertyName("nominal_analysis_items")]
        public List<NominalAnalysisItem>? NominalAnalysisItems { get; set; }

        /// <summary>
        /// Optional external references to persist with the microservice (not sent to Sage).
        /// </summary>
        public List<ExternalRefItem>? ExternalRefs { get; set; }

        public sealed class ExternalRefItem
        {
            public int? AppId { get; set; }
            [Required, MaxLength(200)]
            public string ExternalRef { get; set; } = string.Empty;
        }

        public sealed class TaxAnalysisItem
        {
            [Required]
            [Range(1, long.MaxValue)]
            [JsonPropertyName("id")]
            public long Id { get; set; } // tax code record id

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
    }
}
