using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// Represents the 'nominal_analysis_item' schema within sales_invoice/sales_credit_note etc.
    /// Based on sales.json definitions.
    /// </summary>
    public class SageNominalAnalysisItem
    {
        [Required]
        [MaxLength(8)]
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty; // Nominal Code

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
