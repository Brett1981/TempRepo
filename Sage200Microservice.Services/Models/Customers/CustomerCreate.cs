// =========================================================================================================
// Models/Customers/CustomerCreate.cs — Strongly-typed OpenAPI "customer" (writeable fields only)
// - This is a *direct* OpenAPI-aligned model for bulk/import scenarios.
// - ReadOnly fields are intentionally excluded.
// - We keep everything nullable so we can omit-null during upstream POST.
// .NET 9+
// =========================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Customers
{
    /// <summary>
    /// OpenAPI-aligned payload to POST a Customer directly to Sage 200.
    /// </summary>
    public sealed class CustomerCreate
    {
        // ---------- Required ----------
        /// <summary>Customer account reference (unless "generate automatically" is enabled in Sage).</summary>
        [MaxLength(8)]
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        /// <summary>Customer name.</summary>
        [Required, MaxLength(60)]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        // ---------- Core ----------
        [MaxLength(8)]
        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("on_hold")]
        public bool? OnHold { get; set; }

        [MaxLength(256)]
        [JsonPropertyName("status_reason")]
        public string? StatusReason { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("account_status_type")]
        public string? AccountStatusType { get; set; } // e.g. AccountStatusActive

        [JsonPropertyName("currency_id")]
        public long? CurrencyId { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("exchange_rate_type")]
        public string? ExchangeRateType { get; set; } // e.g. ExchangeRateSingle

        [MaxLength(5)]
        [JsonPropertyName("telephone_country_code")]
        public string? TelephoneCountryCode { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("telephone_area_code")]
        public string? TelephoneAreaCode { get; set; }

        [MaxLength(200)]
        [JsonPropertyName("telephone_subscriber_number")]
        public string? TelephoneSubscriberNumber { get; set; }

        [MaxLength(5)]
        [JsonPropertyName("fax_country_code")]
        public string? FaxCountryCode { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("fax_area_code")]
        public string? FaxAreaCode { get; set; }

        [MaxLength(200)]
        [JsonPropertyName("fax_subscriber_number")]
        public string? FaxSubscriberNumber { get; set; }

        [MaxLength(200)]
        [JsonPropertyName("website")]
        public string? Website { get; set; }

        [JsonPropertyName("credit_limit")]
        public decimal? CreditLimit { get; set; }

        [JsonPropertyName("country_code_id")]
        public long? CountryCodeId { get; set; }

        [JsonPropertyName("default_tax_code_id")]
        public long? DefaultTaxCodeId { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("vat_number")]
        public string? VatNumber { get; set; }

        [MaxLength(9)]
        [JsonPropertyName("duns_code")]
        public string? DunsCode { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("account_type")]
        public string? AccountType { get; set; } // e.g. TradingAccountTypeOpenItem

        [JsonPropertyName("early_settlement_discount_percent")]
        public decimal? EarlySettlementDiscountPercent { get; set; }

        [JsonPropertyName("early_settlement_discount_days")]
        public short? EarlySettlementDiscountDays { get; set; }

        [JsonPropertyName("payment_terms_days")]
        public short? PaymentTermsDays { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("payment_terms_basis")]
        public string? PaymentTermsBasis { get; set; }

        [JsonPropertyName("terms_agreed")]
        public bool? TermsAgreed { get; set; }

        [JsonPropertyName("credit_bureau_id")]
        public long? CreditBureauId { get; set; }

        [JsonPropertyName("credit_position_id")]
        public long? CreditPositionId { get; set; }

        // Professional-only (still allowed to be sent if tenant supports)
        [JsonPropertyName("finance_charge_id")]
        public long? FinanceChargeId { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("trading_terms")]
        public string? TradingTerms { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("credit_reference")]
        public string? CreditReference { get; set; }

        [JsonPropertyName("account_opened")]
        public DateTimeOffset? AccountOpened { get; set; }

        [JsonPropertyName("last_credit_review")]
        public DateTimeOffset? LastCreditReview { get; set; }

        [JsonPropertyName("next_credit_review")]
        public DateTimeOffset? NextCreditReview { get; set; }

        [JsonPropertyName("application_date")]
        public DateTimeOffset? ApplicationDate { get; set; }

        [JsonPropertyName("date_received")]
        public DateTimeOffset? DateReceived { get; set; }

        [MaxLength(20)]
        [JsonPropertyName("office_type")]
        public string? OfficeType { get; set; }

        [JsonPropertyName("associated_head_office_id")]
        public long? AssociatedHeadOfficeId { get; set; }

        [JsonPropertyName("produce_statements_for_customer")]
        public bool? ProduceStatementsForCustomer { get; set; }

        [JsonPropertyName("is_head_office_with_branches")]
        public bool? IsHeadOfficeWithBranches { get; set; }

        [JsonPropertyName("use_consolidated_billing")]
        public bool? UseConsolidatedBilling { get; set; }

        [MaxLength(1)]
        [JsonPropertyName("order_priority")]
        public string? OrderPriority { get; set; }

        [JsonPropertyName("use_tax_code_as_default")]
        public bool? UseTaxCodeAsDefault { get; set; }

        [JsonPropertyName("months_to_keep_transactions")]
        public short? MonthsToKeepTransactions { get; set; }

        [MaxLength(8)]
        [JsonPropertyName("default_nominal_code_reference")]
        public string? DefaultNominalCodeReference { get; set; }

        [MaxLength(3)]
        [JsonPropertyName("default_nominal_code_cost_centre")]
        public string? DefaultNominalCodeCostCentre { get; set; }

        [MaxLength(3)]
        [JsonPropertyName("default_nominal_code_department")]
        public string? DefaultNominalCodeDepartment { get; set; }

        [JsonPropertyName("invoice_discount_percent")]
        public decimal? InvoiceDiscountPercent { get; set; }

        [JsonPropertyName("invoice_line_discount_percent")]
        public decimal? InvoiceLineDiscountPercent { get; set; }

        [JsonPropertyName("customer_discount_group_id")]
        public long? CustomerDiscountGroupId { get; set; }

        [JsonPropertyName("order_value_discount_id")]
        public long? OrderValueDiscountId { get; set; }

        [JsonPropertyName("price_band_id")]
        public long? PriceBandId { get; set; }

        // Analysis codes 1..20
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_1")] public string? AnalysisCode1 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_2")] public string? AnalysisCode2 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_3")] public string? AnalysisCode3 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_4")] public string? AnalysisCode4 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_5")] public string? AnalysisCode5 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_6")] public string? AnalysisCode6 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_7")] public string? AnalysisCode7 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_8")] public string? AnalysisCode8 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_9")] public string? AnalysisCode9 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_10")] public string? AnalysisCode10 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_11")] public string? AnalysisCode11 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_12")] public string? AnalysisCode12 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_13")] public string? AnalysisCode13 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_14")] public string? AnalysisCode14 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_15")] public string? AnalysisCode15 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_16")] public string? AnalysisCode16 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_17")] public string? AnalysisCode17 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_18")] public string? AnalysisCode18 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_19")] public string? AnalysisCode19 { get; set; }
        [MaxLength(60)]
        [JsonPropertyName("analysis_code_20")] public string? AnalysisCode20 { get; set; }

        // Spare text 1..10
        [MaxLength(100)]
        [JsonPropertyName("spare_text_1")] public string? SpareText1 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_2")] public string? SpareText2 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_3")] public string? SpareText3 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_4")] public string? SpareText4 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_5")] public string? SpareText5 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_6")] public string? SpareText6 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_7")] public string? SpareText7 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_8")] public string? SpareText8 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_9")] public string? SpareText9 { get; set; }
        [MaxLength(100)]
        [JsonPropertyName("spare_text_10")] public string? SpareText10 { get; set; }

        // Spare numbers 1..10 (5dp)
        [JsonPropertyName("spare_number_1")] public decimal? SpareNumber1 { get; set; }
        [JsonPropertyName("spare_number_2")] public decimal? SpareNumber2 { get; set; }
        [JsonPropertyName("spare_number_3")] public decimal? SpareNumber3 { get; set; }
        [JsonPropertyName("spare_number_4")] public decimal? SpareNumber4 { get; set; }
        [JsonPropertyName("spare_number_5")] public decimal? SpareNumber5 { get; set; }
        [JsonPropertyName("spare_number_6")] public decimal? SpareNumber6 { get; set; }
        [JsonPropertyName("spare_number_7")] public decimal? SpareNumber7 { get; set; }
        [JsonPropertyName("spare_number_8")] public decimal? SpareNumber8 { get; set; }
        [JsonPropertyName("spare_number_9")] public decimal? SpareNumber9 { get; set; }
        [JsonPropertyName("spare_number_10")] public decimal? SpareNumber10 { get; set; }

        // Spare dates 1..5
        [JsonPropertyName("spare_date_1")] public DateTimeOffset? SpareDate1 { get; set; }
        [JsonPropertyName("spare_date_2")] public DateTimeOffset? SpareDate2 { get; set; }
        [JsonPropertyName("spare_date_3")] public DateTimeOffset? SpareDate3 { get; set; }
        [JsonPropertyName("spare_date_4")] public DateTimeOffset? SpareDate4 { get; set; }
        [JsonPropertyName("spare_date_5")] public DateTimeOffset? SpareDate5 { get; set; }

        // Spare bools 1..5
        [JsonPropertyName("spare_bool_1")] public bool? SpareBool1 { get; set; }
        [JsonPropertyName("spare_bool_2")] public bool? SpareBool2 { get; set; }
        [JsonPropertyName("spare_bool_3")] public bool? SpareBool3 { get; set; }
        [JsonPropertyName("spare_bool_4")] public bool? SpareBool4 { get; set; }
        [JsonPropertyName("spare_bool_5")] public bool? SpareBool5 { get; set; }

        // Address
        [JsonPropertyName("main_address")]
        public MainAddress? Address { get; set; }

        /// <summary>Main Address block.</summary>
        public sealed class MainAddress
        {
            [JsonPropertyName("address_1")] public string? Address1 { get; set; }
            [JsonPropertyName("address_2")] public string? Address2 { get; set; }
            [JsonPropertyName("address_3")] public string? Address3 { get; set; }
            [JsonPropertyName("address_4")] public string? Address4 { get; set; }
            [JsonPropertyName("city")] public string? City { get; set; }
            [JsonPropertyName("county")] public string? County { get; set; }
            [JsonPropertyName("postcode")] public string? Postcode { get; set; }
            [JsonPropertyName("address_country_code_id")]
            public long? AddressCountryCodeId { get; set; }
        }
    }
}
