using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// Represents the 'tax_analysis_item' schema within sales_invoice/sales_credit_note.
    /// Based on sales.json definitions.
    /// </summary>
    public class SageTaxAnalysisItem
    {
        [Required]
        [Range(1, long.MaxValue)]
        [JsonPropertyName("id")]
        public long Id { get; set; } // Tax Code ID

        [JsonPropertyName("goods_amount")]
        public decimal? GoodsAmount { get; set; }

        [JsonPropertyName("discount_amount")]
        public decimal? DiscountAmount { get; set; }

        [JsonPropertyName("tax_amount")]
        public decimal? TaxAmount { get; set; }

        [JsonPropertyName("tax_discount_amount")]
        public decimal? TaxDiscountAmount { get; set; }
    }
}
