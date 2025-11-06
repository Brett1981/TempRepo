using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// Represents the 'sop_order_line' schema from sop.json.
    /// Includes properties for GET, POST, and PUT. ReadOnly properties marked as such.
    /// </summary>
    public class SageSopOrderLine
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } // ReadOnly

        [JsonPropertyName("sop_order_id")]
        public long? SopOrderId { get; set; } // ReadOnly

        [MaxLength(20)]
        [JsonPropertyName("line_type")]
        public string? LineType { get; set; } // e.g., "EnumLineTypeStandard"

        [JsonPropertyName("product_id")]
        public long? ProductId { get; set; } // Required for Standard lines

        [JsonPropertyName("warehouse_id")]
        public long? WarehouseId { get; set; } // Required for Standard lines

        [JsonPropertyName("create_cancelled_line")]
        public bool? CreateCancelledLine { get; set; }

        [JsonPropertyName("is_to_split_line")]
        public bool? IsToSplitLine { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("code")]
        public string? Code { get; set; } // Stock item code or Charge code

        [JsonPropertyName("use_description")]
        public bool? UseDescription { get; set; }

        [MaxLength(6000)]
        [JsonPropertyName("description")]
        public string? Description { get; set; } // Required for FreeText, Comment, Charge (if code omitted)

        [JsonPropertyName("line_quantity")]
        public decimal? LineQuantity { get; set; } // For Standard, FreeText

        [JsonPropertyName("to_allocate_quantity")]
        public decimal? ToAllocateQuantity { get; set; } // For Standard

        [JsonPropertyName("line_number")]
        public short? LineNumber { get; set; } // ReadOnly (set via is_to_sequence_lines)

        [JsonPropertyName("tax_code_id")]
        public long? TaxCodeId { get; set; } // For Standard, FreeText, Charge

        [MaxLength(8)]
        [JsonPropertyName("nominal_reference")]
        public string? NominalReference { get; set; } // For Standard, FreeText, Charge

        [MaxLength(3)]
        [JsonPropertyName("nominal_cost_centre")]
        public string? NominalCostCentre { get; set; } // For Standard, FreeText, Charge

        [MaxLength(3)]
        [JsonPropertyName("nominal_department")]
        public string? NominalDepartment { get; set; } // For Standard, FreeText, Charge

        [JsonPropertyName("allocated_quantity")]
        public decimal? AllocatedQuantity { get; set; } // ReadOnly? Check API docs if settable

        [JsonPropertyName("available_for_despatch")]
        public decimal? AvailableForDespatch { get; set; } // ReadOnly

        [JsonPropertyName("despatch_receipt_quantity")]
        public decimal? DespatchReceiptQuantity { get; set; } // ReadOnly

        [JsonPropertyName("invoice_credit_quantity")]
        public decimal? InvoiceCreditQuantity { get; set; } // ReadOnly

        [JsonPropertyName("posted_invoice_credit_quantity")]
        public decimal? PostedInvoiceCreditQuantity { get; set; } // ReadOnly

        // ... other read-only quantity fields ...

        [MaxLength(20)]
        [JsonPropertyName("allocation_status")]
        public string? AllocationStatus { get; set; } // ReadOnly

        [MaxLength(20)]
        [JsonPropertyName("despatch_receipt_status")]
        public string? DespatchReceiptStatus { get; set; } // ReadOnly

        [MaxLength(20)]
        [JsonPropertyName("invoice_credit_status")]
        public string? InvoiceCreditStatus { get; set; } // ReadOnly

        [JsonPropertyName("selling_unit_id")]
        public long? SellingUnitId { get; set; } // For Standard (Stock)

        [MaxLength(20)]
        [JsonPropertyName("selling_unit_description")]
        public string? SellingUnitDescription { get; set; } // For FreeText

        [JsonPropertyName("selling_unit_price")]
        public decimal? SellingUnitPrice { get; set; } // For Standard, FreeText, Charge

        [JsonPropertyName("selling_unit_price_overridden")]
        public bool? SellingUnitPriceOverridden { get; set; } // For Standard

        [JsonPropertyName("pricing_unit_id")]
        public long? PricingUnitId { get; set; } // For Standard (Stock)

        [MaxLength(20)]
        [JsonPropertyName("pricing_unit_description")]
        public string? PricingUnitDescription { get; set; } // For Standard (Non-Stock)

        // ... unit precision/multiple fields (ReadOnly) ...

        [JsonPropertyName("unit_discount_percent")]
        public decimal? UnitDiscountPercent { get; set; } // For Standard, FreeText

        [JsonPropertyName("discount_percent_specified")]
        public bool? DiscountPercentSpecified { get; set; } // For Standard, FreeText

        [JsonPropertyName("unit_discount_value")]
        public decimal? UnitDiscountValue { get; set; } // For Standard, FreeText

        [JsonPropertyName("unit_discount_overridden")]
        public bool? UnitDiscountOverridden { get; set; } // For Standard

        [JsonPropertyName("discounted_unit_price")]
        public decimal? DiscountedUnitPrice { get; set; } // For Standard, FreeText

        [JsonPropertyName("cost_price")]
        public decimal? CostPrice { get; set; } // For Standard, FreeText, Charge

        [JsonPropertyName("retain_manual_prices")]
        public bool? RetainManualPrices { get; set; } // For Standard

        [MaxLength(50)]
        [JsonPropertyName("fulfilment_method")]
        public string? FulfilmentMethod { get; set; } // For Standard

        [MaxLength(50)]
        [JsonPropertyName("confirmation_intent_type")]
        public string? ConfirmationIntentType { get; set; } // For Standard (Service/Labour), FreeText

        [JsonPropertyName("mark_as_preferred")]
        public bool? MarkAsPreferred { get; set; } // For Standard

        [MaxLength(160)]
        [JsonPropertyName("picking_list_comment")]
        public string? PickingListComment { get; set; } // For Standard, FreeText

        [MaxLength(160)]
        [JsonPropertyName("despatch_note_comment")]
        public string? DespatchNoteComment { get; set; } // For Standard, FreeText

        [JsonPropertyName("show_on_customer_docs")]
        public bool? ShowOnCustomerDocs { get; set; } // For Comment

        [MaxLength(20)]
        [JsonPropertyName("show_on_picking_list_type")]
        public string? ShowOnPickingListType { get; set; } // For Comment

        [JsonPropertyName("has_pop_order")]
        public bool? HasPopOrder { get; set; } // ReadOnly

        [MaxLength(20)]
        [JsonPropertyName("back_to_back_status")]
        public string? BackToBackStatus { get; set; } // ReadOnly

        [JsonPropertyName("is_complete")]
        public bool? IsComplete { get; set; } // ReadOnly

        [JsonPropertyName("is_line_deletable")]
        public bool? IsLineDeletable { get; set; } // ReadOnly

        [JsonPropertyName("line_tax_value")]
        public decimal? LineTaxValue { get; set; } // ReadOnly

        [JsonPropertyName("line_total_value")]
        public decimal? LineTotalValue { get; set; } // ReadOnly

        [JsonPropertyName("requested_delivery_date")]
        public DateTimeOffset? RequestedDeliveryDate { get; set; } // For Standard, FreeText

        [JsonPropertyName("promised_delivery_date")]
        public DateTimeOffset? PromisedDeliveryDate { get; set; } // For Standard, FreeText

        [MaxLength(60)]
        [JsonPropertyName("analysis_code_1")] public string? AnalysisCode1 { get; set; }
        // ... Analysis Codes 2-20 ...
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_20")] public string? AnalysisCode20 { get; set; }

        [MaxLength(100)]
        [JsonPropertyName("spare_text_1")] public string? SpareText1 { get; set; }
        // ... Spare Text 2-10 ...
        [MaxLength(100)]
        [JsonPropertyName("spare_text_10")] public string? SpareText10 { get; set; }

        [JsonPropertyName("spare_number_1")] public decimal? SpareNumber1 { get; set; }
        // ... Spare Numbers 2-10 ...
        [JsonPropertyName("spare_number_10")] public decimal? SpareNumber10 { get; set; }

        [JsonPropertyName("spare_date_1")] public DateTimeOffset? SpareDate1 { get; set; }
        // ... Spare Dates 2-5 ...
        [JsonPropertyName("spare_date_5")] public DateTimeOffset? SpareDate5 { get; set; }

        [JsonPropertyName("spare_bool_1")] public bool? SpareBool1 { get; set; }
        // ... Spare Bools 2-5 ...
        [JsonPropertyName("spare_bool_5")] public bool? SpareBool5 { get; set; }


        // Omitting ReadOnly nested objects (product, warehouse, tax_code etc.) for create/update clarity
        // Include traceable_adjustment_items if needed

        [JsonPropertyName("is_to_delete")]
        public bool? IsToDelete { get; set; } // For PUT operations

        [JsonPropertyName("date_time_created")]
        public DateTimeOffset? DateTimeCreated { get; set; } // ReadOnly

        [JsonPropertyName("date_time_updated")]
        public DateTimeOffset? DateTimeUpdated { get; set; } // ReadOnly
    }
}
