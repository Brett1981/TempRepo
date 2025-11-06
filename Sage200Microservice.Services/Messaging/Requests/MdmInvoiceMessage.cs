using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Messaging.Requests
{
    /// <summary>
    /// Root payload carried on MDM_INVOICE. This represents a single invoice request
    /// (customer upsert + SOP order create which will generate a sales invoice in Sage).
    /// </summary>
    public sealed class MdmInvoiceMessage
    {
        /// <summary>
        /// External reference from the calling app (used for ExternalIdLink).
        /// Strongly recommended. Will be stored with ApiKeys.Id scope.
        /// </summary>
        [Required]
        public string ExternalRef { get; set; } = string.Empty;

        /// <summary>
        /// A unique invoice number supplied by the calling app that will be used for the Sage document number.
        /// If you generate this in CymBuild, pass it here so we can propagate it to SOP/invoice.
        /// </summary>
        [Required]
        public string InvoiceNumber { get; set; } = string.Empty;

        /// <summary>
        /// Details for matching/creating/updating a Sage customer.
        /// </summary>
        [Required]
        public CustomerPayload Customer { get; set; } = new();

        /// <summary>
        /// The SOP order to create. An invoice will be posted from this SOP.
        /// </summary>
        [Required]
        public SopOrderPayload SopOrder { get; set; } = new();

        /// <summary>
        /// Optional idempotency key. If provided, we’ll hash & use for idempotent processing.
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>
        /// Optional UTC timestamp from the caller; if omitted we’ll set server time.
        /// </summary>
        public DateTimeOffset? RequestedAtUtc { get; set; }
    }

    /// <summary>
    /// Minimal customer payload needed to upsert a Sage customer.
    /// </summary>
    public sealed class CustomerPayload
    {
        /// <summary>
        /// Your preferred account code/reference. If omitted, we can generate from name.
        /// Sage: customer.reference
        /// </summary>
        public string? CustomerReference { get; set; }

        /// <summary>
        /// Customer account name.
        /// Sage: customer.name
        /// </summary>
        [Required]
        [MaxLength(180)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Main address for the customer account (billing).
        /// Sage: customer.main_address.*
        /// </summary>
        [Required]
        public CustomerAddressPayload Address { get; set; } = new();

        /// <summary>
        /// One primary contact. We’ll create/update a single contact with default email/phone.
        /// Sage: customer_contact + customer_email (emails[0])
        /// </summary>
        public CustomerContactPayload? PrimaryContact { get; set; }
    }

    /// <summary>
    /// Maps to sales.json: customer.main_address
    /// </summary>
    public sealed class CustomerAddressPayload
    {
        /// <summary> Sage: address_1 </summary>
        [Required, MaxLength(60)]
        public string AddressLine1 { get; set; } = string.Empty;

        /// <summary> Sage: address_2 </summary>
        [MaxLength(60)]
        public string? AddressLine2 { get; set; }

        /// <summary> Sage: city </summary>
        [Required, MaxLength(60)]
        public string City { get; set; } = string.Empty;

        /// <summary> Sage: postcode </summary>
        [Required, MaxLength(10)]
        public string Postcode { get; set; } = string.Empty;

        /// <summary>
        /// Free-text country name (read-only on GET in some schemas, but we accept and map if allowed).
        /// Sage: country (when segmented addresses are enabled). If Sage rejects, we’ll ignore.
        /// </summary>
        [MaxLength(60)]
        public string? Country { get; set; }
    }

    /// <summary>
    /// Maps to sales.json: customer_contact + emails[]
    /// </summary>
    public sealed class CustomerContactPayload
    {
        /// <summary> Sage: customer_contact.first_name + last_name (we’ll split heuristically if needed) </summary>
        [MaxLength(180)]
        public string? ContactName { get; set; }

        /// <summary> Sage: customer_email.email (as default email for this contact) </summary>
        [MaxLength(227)]
        public string? Email { get; set; }

        /// <summary> Sage: default_telephone or telephones[0].telephone (we’ll pick supported path) </summary>
        [MaxLength(227)]
        public string? Telephone { get; set; }
    }

    /// <summary>
    /// SOP Order header + lines. We’ll create this in Sage, then use it to generate/post the Sales Invoice.
    /// </summary>
    public sealed class SopOrderPayload
    {
        /// <summary>
        /// If you already know the Sage customer reference (account code), pass it here; else we’ll use
        /// CustomerPayload.CustomerReference (post-upsert) to populate this.
        /// Sage: sop_order.customer_reference
        /// </summary>
        public string? CustomerReference { get; set; }

        /// <summary>
        /// The external invoice number that must be used as the Sage document number.
        /// We’ll pass this through so SOP/Invoice uses the exact number.
        /// Sage: document_no / order_no
        /// </summary>
        [Required]
        public string DocumentNumber { get; set; } = string.Empty;

        /// <summary>
        /// Currency code (e.g., "GBP"). If omitted, Sage company default is used.
        /// Sage: currency_code
        /// </summary>
        [MaxLength(3)]
        public string? CurrencyCode { get; set; }

        /// <summary>
        /// Optional delivery address; if omitted, Sage uses customer’s default.
        /// Sage: delivery_address.*
        /// </summary>
        public SopDeliveryAddressPayload? DeliveryAddress { get; set; }

        /// <summary>
        /// SOP order lines (at least one).
        /// Sage: sop_order_lines[]
        /// </summary>
        [Required]
        [MinLength(1)]
        public List<SopOrderLinePayload> Lines { get; set; } = new();
    }

    /// <summary>
    /// SOP delivery address mapping: sop_order.delivery_address.*
    /// </summary>
    public sealed class SopDeliveryAddressPayload
    {
        [MaxLength(60)] public string? AddressLine1 { get; set; }
        [MaxLength(60)] public string? AddressLine2 { get; set; }
        [MaxLength(60)] public string? City { get; set; }
        [MaxLength(10)] public string? Postcode { get; set; }
        [MaxLength(60)] public string? Country { get; set; }
    }

    /// <summary>
    /// One SOP order line. Supports stock (by code) or nominal/service lines.
    /// </summary>
    public sealed class SopOrderLinePayload
    {
        /// <summary>
        /// If provided we’ll treat as a stock item line (Sage: stock_item_code). Otherwise we’ll treat it as a free-text/nominal line.
        /// </summary>
        [MaxLength(60)]
        public string? StockItemCode { get; set; }

        /// <summary> Free-text description (required for non-stock lines). Sage: text </summary>
        [MaxLength(255)]
        public string? Description { get; set; }

        /// <summary> Quantity ordered. Sage: quantity_ordered </summary>
        [Required]
        public decimal Quantity { get; set; }

        /// <summary> Unit price (net). Sage: unit_price </summary>
        [Required]
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// Nominal code for non-stock lines. For stock lines, Sage resolves the nominal from stock settings.
        /// Sage: nominal_code (when applicable)
        /// </summary>
        [MaxLength(60)]
        public string? NominalCode { get; set; }

        /// <summary>
        /// Tax code or rate identifier. We’ll translate this into Sage tax analysis on create.
        /// </summary>
        [MaxLength(10)]
        public string? TaxCode { get; set; }
    }
}
