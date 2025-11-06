using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// Represents the 'sales_invoice' schema from sales.json, primarily for POST operations.
    /// </summary>
    public class SageSalesInvoice
    {
        [Required]
        [Range(1, long.MaxValue)]
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }

        [JsonPropertyName("transaction_date")]
        public DateTimeOffset? TransactionDate { get; set; } // Defaults to system date if null

        [JsonPropertyName("due_date")]
        public DateTimeOffset? DueDate { get; set; }

        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; } // Defaults to customer rate

        [MaxLength(40)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [MaxLength(40)]
        [JsonPropertyName("second_reference")]
        public string? SecondReference { get; set; }

        [JsonPropertyName("settled_immediately")]
        public bool? SettledImmediately { get; set; }

        // Required Values (must be provided and add up correctly)
        [JsonPropertyName("document_goods_value")]
        public decimal? DocumentGoodsValue { get; set; } // Required if NominalAnalysisItems are provided and need summing

        [JsonPropertyName("document_tax_value")]
        public decimal? DocumentTaxValue { get; set; } // Required if TaxAnalysisItems are provided and need summing

        [JsonPropertyName("document_discount_value")]
        public decimal? DocumentDiscountValue { get; set; }

        [JsonPropertyName("document_tax_discount_value")]
        public decimal? DocumentTaxDiscountValue { get; set; }

        [JsonPropertyName("discount_percent")]
        public decimal? DiscountPercent { get; set; } // Defaults from customer

        [JsonPropertyName("discount_days")]
        public short? DiscountDays { get; set; } // Defaults from customer

        [JsonPropertyName("triangular_transaction")]
        public bool? TriangularTransaction { get; set; }

        [JsonPropertyName("tax_analysis_items")]
        public List<SageTaxAnalysisItem>? TaxAnalysisItems { get; set; }

        [JsonPropertyName("nominal_analysis_items")]
        public List<SageNominalAnalysisItem>? NominalAnalysisItems { get; set; }

        // is_eu_trader is ReadOnly
    }
}
