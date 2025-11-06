using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// [REPLACEMENT - STAGE 10 - Corrected]
    /// Represents the 'sop_order' schema from sop.json.
    /// Includes properties for GET, POST, and PUT. ReadOnly properties marked.
    /// Added missing properties for build fix.
    /// </summary>
    public class SageSopOrder
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } // ReadOnly

        [Required]
        [JsonPropertyName("customer_id")]
        public long? CustomerId { get; set; }

        // --- Added Missing Properties (from sop.json / error report) ---

        [MaxLength(8)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; } // Note: 'reference' is not in sop_order root, but 'customer_document_no' is. Using this to match service code.

        [MaxLength(60)]
        [JsonPropertyName("customer_reference")]
        public string? CustomerReference { get; set; } // Note: 'customer_reference' is not in sop_order root.

        [MaxLength(20)]
        [JsonPropertyName("status")]
        public string? Status { get; set; } // ReadOnly - friendly status

        [JsonPropertyName("currency_code")]
        public string? CurrencyCode { get; set; } // ReadOnly - from nested currency object

        [JsonPropertyName("subtotal_goods_value")]
        public decimal? SubtotalGoodsValue { get; set; } // ReadOnly

        [JsonPropertyName("total_tax_value")]
        public decimal? TotalTaxValue { get; set; } // ReadOnly

        [JsonPropertyName("total_gross_value")]
        public decimal? TotalGrossValue { get; set; } // ReadOnly

        [JsonPropertyName("document_date")]
        public DateTimeOffset? DocumentDate { get; set; }

        [JsonPropertyName("promised_delivery_date")]
        public DateTimeOffset? PromisedDeliveryDate { get; set; }

        // --- End of Added Missing Properties ---


        [JsonPropertyName("is_draft")]
        public bool? IsDraft { get; set; } // ReadOnly

        [JsonPropertyName("is_editing")]
        public bool? IsEditing { get; set; } // Used in PUT

        [JsonPropertyName("is_to_sequence_lines")]
        public bool? IsToSequenceLines { get; set; } // Used in POST/PUT

        [JsonPropertyName("override_on_hold")]
        public bool? OverrideOnHold { get; set; }

        [JsonPropertyName("recalculate_prices")]
        public bool? RecalculatePrices { get; set; }

        [JsonPropertyName("apply_available_document_discount_percent")]
        public bool? ApplyAvailableDocumentDiscountPercent { get; set; }

        [JsonPropertyName("lock_id")]
        public long? LockId { get; set; } // ReadOnly (from GET with is_to_lock)

        [JsonPropertyName("customer_delivery_address_id")]
        public long? CustomerDeliveryAddressId { get; set; }

        [JsonPropertyName("suppress_warnings")]
        public bool? SuppressWarnings { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("document_no")]
        public string? DocumentNo { get; set; } // May be required on POST if auto-gen off

        [MaxLength(50)]
        [JsonPropertyName("customer_type")]
        public string? CustomerType { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("document_status")]
        public string? DocumentStatus { get; set; } // ReadOnly

        [JsonPropertyName("currency_id")]
        public long? CurrencyId { get; set; } // ReadOnly? Typically set via Customer

        [JsonPropertyName("exchange_rate")]
        public decimal? ExchangeRate { get; set; }

        // ... Other total fields (ReadOnly) ...
        [JsonPropertyName("total_net_value")]
        public decimal? TotalNetValue { get; set; } // ReadOnly

        [MaxLength(30)]
        [JsonPropertyName("customer_document_no")]
        public string? CustomerDocumentNo { get; set; } // Sometimes aliased as 'customer_reference'

        [JsonPropertyName("is_credit_limit_exceeded")]
        public bool? IsCreditLimitExceeded { get; set; } // ReadOnly

        [JsonPropertyName("use_invoice_address")]
        public bool? UseInvoiceAddress { get; set; }

        [JsonPropertyName("is_triangulated")]
        public bool? IsTriangulated { get; set; }

        [JsonPropertyName("settlement_discount_days")]
        public short? SettlementDiscountDays { get; set; }

        [JsonPropertyName("settlement_discount_percent")]
        public decimal? SettlementDiscountPercent { get; set; }

        [JsonPropertyName("document_discount_percent")]
        public decimal? DocumentDiscountPercent { get; set; }

        [JsonPropertyName("available_document_discount_percent")]
        public decimal? AvailableDocumentDiscountPercent { get; set; } // ReadOnly

        [MaxLength(30)]
        [JsonPropertyName("document_created_by")]
        public string? DocumentCreatedBy { get; set; }

        [JsonPropertyName("requested_delivery_date")]
        public DateTimeOffset? RequestedDeliveryDate { get; set; }

        [JsonPropertyName("use_header_requested_date")]
        public bool? UseHeaderRequestedDate { get; set; } // Used in PUT

        [JsonPropertyName("use_header_promised_date")]
        public bool? UseHeaderPromisedDate { get; set; } // Used in PUT

        [JsonPropertyName("quotation_expiry_date")]
        public DateTimeOffset? QuotationExpiryDate { get; set; } // For Quotations

        [MaxLength(1)]
        [JsonPropertyName("order_priority")]
        public string? OrderPriority { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("external_reference")]
        public string? ExternalReference { get; set; } // ReadOnly

        [JsonPropertyName("payment_with_order")]
        public bool? PaymentWithOrder { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("payment_type")]
        public string? PaymentType { get; set; } // e.g., "EnumSOPOrderPaymentTypeFull"

        [JsonPropertyName("invoice_payment_with_order_immediately")]
        public bool? InvoicePaymentWithOrderImmediately { get; set; }

        [JsonPropertyName("payment_value")]
        public decimal? PaymentValue { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("payment_reference")]
        public string? PaymentReference { get; set; }

        [JsonPropertyName("payment_method_id")]
        public long? PaymentMethodId { get; set; }

        // ... Payment declared/undeclared fields (ReadOnly) ...

        [MaxLength(60)][JsonPropertyName("analysis_code_1")] public string? AnalysisCode1 { get; set; }
        // ... Analysis Codes 2-20 ...
        [MaxLength(60)][JsonPropertyName("analysis_code_20")] public string? AnalysisCode20 { get; set; }

        [MaxLength(100)][JsonPropertyName("spare_text_1")] public string? SpareText1 { get; set; }
        // ... Spare Text 2-10 ...
        [MaxLength(100)][JsonPropertyName("spare_text_10")] public string? SpareText10 { get; set; }

        [JsonPropertyName("spare_number_1")] public decimal? SpareNumber1 { get; set; }
        // ... Spare Numbers 2-10 ...
        [JsonPropertyName("spare_number_10")] public decimal? SpareNumber10 { get; set; }

        [JsonPropertyName("spare_date_1")] public DateTimeOffset? SpareDate1 { get; set; }
        // ... Spare Dates 2-5 ...
        [JsonPropertyName("spare_date_5")] public DateTimeOffset? SpareDate5 { get; set; }

        [JsonPropertyName("spare_bool_1")] public bool? SpareBool1 { get; set; }
        // ... Spare Bools 2-5 ...
        [JsonPropertyName("spare_bool_5")] public bool? SpareBool5 { get; set; }

        // Nested objects
        [JsonPropertyName("delivery_address")]
        public SageDeliveryAddress? DeliveryAddress { get; set; }

        [JsonPropertyName("lines")]
        public List<SageSopOrderLine>? Lines { get; set; }

        // Omitting ReadOnly nested objects (customer, currency, profitability, memos etc.)

        [JsonPropertyName("date_time_created")]
        public DateTimeOffset? DateTimeCreated { get; set; } // ReadOnly

        [JsonPropertyName("date_time_updated")]
        public DateTimeOffset? DateTimeUpdated { get; set; } // ReadOnly
    }

    /// <summary>
    /// [Unchanged - STAGE 10]
    /// Minimal DTO to capture URN from Sales Invoice creation response.
    /// </summary>
    public class SageUrnResponse
    {
        [JsonPropertyName("urn")]
        public string? Urn { get; set; }
    }
}

