// =========================================================================================================
// Models/Views/CustomerSopInvoiceCreditLineViewDto.cs
// - Strongly typed DTO for the "customer_sop_invoice_credit_line_view" resource.
// - Property names map 1:1 to Sage 200 snake_case fields via JsonPropertyName.
// - Intended for read/query only.
// =========================================================================================================

using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Views
{
    /// <summary>
    /// View of customer + SOP invoice credit + line + profit analysis + product + product group.
    /// </summary>
    public sealed class CustomerSopInvoiceCreditLineViewDto
    {
        [JsonPropertyName("customer_id")] public long? CustomerId { get; set; }
        [JsonPropertyName("customer_reference")] public string? CustomerReference { get; set; }
        [JsonPropertyName("customer_name")] public string? CustomerName { get; set; }

        [JsonPropertyName("sop_invoice_credit_id")] public long? SopInvoiceCreditId { get; set; }
        [JsonPropertyName("sop_invoice_credit_type")] public string? SopInvoiceCreditType { get; set; }
        [JsonPropertyName("sop_invoice_credit_document_no")] public string? SopInvoiceCreditDocumentNo { get; set; }
        [JsonPropertyName("sop_invoice_credit_document_date")] public DateTimeOffset? SopInvoiceCreditDocumentDate { get; set; }
        [JsonPropertyName("sop_invoice_credit_document_status")] public string? SopInvoiceCreditDocumentStatus { get; set; }
        [JsonPropertyName("sop_invoice_credit_exchange_rate")] public decimal? SopInvoiceCreditExchangeRate { get; set; }
        [JsonPropertyName("sop_invoice_credit_date_time_updated")] public DateTimeOffset? SopInvoiceCreditDateTimeUpdated { get; set; }

        [JsonPropertyName("sop_invoice_credit_line_id")] public long? SopInvoiceCreditLineId { get; set; }
        [JsonPropertyName("sop_invoice_credit_line_invoice_credit_date")] public DateTimeOffset? SopInvoiceCreditLineInvoiceCreditDate { get; set; }
        [JsonPropertyName("sop_invoice_credit_line_total_value")] public decimal? SopInvoiceCreditLineTotalValue { get; set; }
        [JsonPropertyName("sop_invoice_credit_line_tax_value")] public decimal? SopInvoiceCreditLineTaxValue { get; set; }
        [JsonPropertyName("sop_invoice_credit_line_date_time_updated")] public DateTimeOffset? SopInvoiceCreditLineDateTimeUpdated { get; set; }

        [JsonPropertyName("invoice_line_profit_analysis_id")] public long? InvoiceLineProfitAnalysisId { get; set; }
        [JsonPropertyName("invoice_line_profit_analysis_line_quantity")] public decimal? InvoiceLineProfitAnalysisLineQuantity { get; set; }
        [JsonPropertyName("invoice_line_profit_analysis_realised_cost_value")] public decimal? InvoiceLineProfitAnalysisRealisedCostValue { get; set; }
        [JsonPropertyName("invoice_line_profit_analysis_realised_profit_value")] public decimal? InvoiceLineProfitAnalysisRealisedProfitValue { get; set; }
        [JsonPropertyName("invoice_line_profit_analysis_date_time_updated")] public DateTimeOffset? InvoiceLineProfitAnalysisDateTimeUpdated { get; set; }

        [JsonPropertyName("product_id")] public long? ProductId { get; set; }
        [JsonPropertyName("product_code")] public string? ProductCode { get; set; }
        [JsonPropertyName("product_name")] public string? ProductName { get; set; }
        [JsonPropertyName("product_description")] public string? ProductDescription { get; set; }
        [JsonPropertyName("product_date_time_updated")] public DateTimeOffset? ProductDateTimeUpdated { get; set; }

        [JsonPropertyName("product_group_id")] public long? ProductGroupId { get; set; }
        [JsonPropertyName("product_group_code")] public string? ProductGroupCode { get; set; }
        [JsonPropertyName("product_group_description")] public string? ProductGroupDescription { get; set; }
        [JsonPropertyName("product_group_date_time_updated")] public DateTimeOffset? ProductGroupDateTimeUpdated { get; set; }
    }
}
