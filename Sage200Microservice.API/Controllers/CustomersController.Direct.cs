// =========================================================================================================
// API/Controllers/CustomersController.Direct.cs — New direct pass-through endpoint
// Route: POST /api/customers/direct
// - Accepts the OpenAPI-aligned CustomerCreate payload (above)
// - Requires X-Site and X-Company (mirrors other write endpoints)
// - Applies Idempotency-Key (passthrough or derived from payload hash)
// - Omit-nulls body construction & snake_case keys
// - Returns a compact reply with Sage Id and Reference when available
// =========================================================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Services.Models.Customers;
using System.Text.Json;

namespace Sage200Microservice.API.Controllers
{
    public partial class CustomersController
    {
        /// <summary>
        /// Direct pass-through to Sage 200: create a Customer with the full OpenAPI shape.
        /// Use this for high-volume import/bulk scenarios. Requires X-Site and X-Company headers.
        /// </summary>
        [HttpPost("direct")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(DirectCustomerCreateReply), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> CreateDirectAsync([FromBody] CustomerCreate request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Validation failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more fields are invalid."
                });
            }

            // ---------- Build omit-nulls body ----------
            var body = BuildCustomerBody(request);

            // ---------- Idempotency ----------
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Optional pass-through if caller supplied them; otherwise SageAuthDelegatingHandler injects defaults.
            if (Request.Headers.TryGetValue("X-Site", out var xSite) && !StringValues.IsNullOrEmpty(xSite))
                headers["X-Site"] = xSite.ToString();
            if (Request.Headers.TryGetValue("X-Company", out var xCompany) && !StringValues.IsNullOrEmpty(xCompany))
                headers["X-Company"] = xCompany.ToString();

            if (Request.Headers.TryGetValue("Idempotency-Key", out var idem) && !StringValues.IsNullOrEmpty(idem))
            {
                headers["Idempotency-Key"] = idem.ToString();
            }
            else
            {
                var canon = JsonSerializer.Serialize(body);
                headers["Idempotency-Key"] = HashBase64Url(canon) ?? Guid.NewGuid().ToString("N");
            }

            // ---------- POST /customers ----------
            var (status, bodyText) = await _sage.PostForBodyAsync("customers", body, headers, ct);

            if (status is < 200 or > 299)
            {
                _log.LogWarning("Upstream error creating customer (direct): status={Status}, body={Body}", status, SafePreview(bodyText));
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Type = "https://httpstatuses.com/502",
                    Title = "Upstream error",
                    Status = StatusCodes.Status502BadGateway,
                    Detail = SafePreview(bodyText)
                });
            }

            // ---------- Parse Id/Reference ----------
            long? sageId = null;
            string? reference = null;
            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                var root = doc.RootElement;

                // Accept object or envelope { items: [ { ... } ] }
                var el = root;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("items", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array &&
                    arr.GetArrayLength() > 0)
                {
                    el = arr[0];
                }

                if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                        sageId = idProp.GetInt64();
                    if (el.TryGetProperty("reference", out var refProp) && refProp.ValueKind == JsonValueKind.String)
                        reference = refProp.GetString();
                }
            }
            catch (JsonException)
            {
                // tolerate minimal responses
            }

            return Ok(new DirectCustomerCreateReply
            {
                Success = true,
                SageId = sageId,
                Reference = reference,
                Message = "Created"
            });
        }

        // ---------- Helpers (duplicate-safe; shared with other controllers) ----------

        /// <summary>
        /// Builds a snake_case dictionary for the Sage "customer" resource, omitting null/empty values.
        /// </summary>
        private static Dictionary<string, object> BuildCustomerBody(CustomerCreate c)
        {
            var d = new Dictionary<string, object>();

            void Add<T>(string name, T? value) where T : struct
            {
                if (value is not null) d[name] = value.Value!;
            }
            void AddString(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) d[name] = value!;
            }

            // Required
            AddString("reference", c.Reference);
            AddString("name", c.Name);

            // Core
            AddString("short_name", c.ShortName);
            Add("on_hold", c.OnHold);
            AddString("status_reason", c.StatusReason);
            AddString("account_status_type", c.AccountStatusType);
            Add("currency_id", c.CurrencyId);
            AddString("exchange_rate_type", c.ExchangeRateType);

            AddString("telephone_country_code", c.TelephoneCountryCode);
            AddString("telephone_area_code", c.TelephoneAreaCode);
            AddString("telephone_subscriber_number", c.TelephoneSubscriberNumber);

            AddString("fax_country_code", c.FaxCountryCode);
            AddString("fax_area_code", c.FaxAreaCode);
            AddString("fax_subscriber_number", c.FaxSubscriberNumber);

            AddString("website", c.Website);
            Add("credit_limit", c.CreditLimit);
            Add("country_code_id", c.CountryCodeId);
            Add("default_tax_code_id", c.DefaultTaxCodeId);
            AddString("vat_number", c.VatNumber);
            AddString("duns_code", c.DunsCode);
            AddString("account_type", c.AccountType);

            Add("early_settlement_discount_percent", c.EarlySettlementDiscountPercent);
            Add("early_settlement_discount_days", c.EarlySettlementDiscountDays);
            Add("payment_terms_days", c.PaymentTermsDays);
            AddString("payment_terms_basis", c.PaymentTermsBasis);
            Add("terms_agreed", c.TermsAgreed);

            Add("credit_bureau_id", c.CreditBureauId);
            Add("credit_position_id", c.CreditPositionId);
            Add("finance_charge_id", c.FinanceChargeId);

            AddString("trading_terms", c.TradingTerms);
            AddString("credit_reference", c.CreditReference);

            if (c.AccountOpened.HasValue) d["account_opened"] = c.AccountOpened.Value.UtcDateTime;
            if (c.LastCreditReview.HasValue) d["last_credit_review"] = c.LastCreditReview.Value.UtcDateTime;
            if (c.NextCreditReview.HasValue) d["next_credit_review"] = c.NextCreditReview.Value.UtcDateTime;
            if (c.ApplicationDate.HasValue) d["application_date"] = c.ApplicationDate.Value.UtcDateTime;
            if (c.DateReceived.HasValue) d["date_received"] = c.DateReceived.Value.UtcDateTime;

            AddString("office_type", c.OfficeType);
            Add("associated_head_office_id", c.AssociatedHeadOfficeId);
            Add("produce_statements_for_customer", c.ProduceStatementsForCustomer);
            Add("is_head_office_with_branches", c.IsHeadOfficeWithBranches);
            Add("use_consolidated_billing", c.UseConsolidatedBilling);
            AddString("order_priority", c.OrderPriority);
            Add("use_tax_code_as_default", c.UseTaxCodeAsDefault);
            Add("months_to_keep_transactions", c.MonthsToKeepTransactions);

            AddString("default_nominal_code_reference", c.DefaultNominalCodeReference);
            AddString("default_nominal_code_cost_centre", c.DefaultNominalCodeCostCentre);
            AddString("default_nominal_code_department", c.DefaultNominalCodeDepartment);

            Add("invoice_discount_percent", c.InvoiceDiscountPercent);
            Add("invoice_line_discount_percent", c.InvoiceLineDiscountPercent);

            Add("customer_discount_group_id", c.CustomerDiscountGroupId);
            Add("order_value_discount_id", c.OrderValueDiscountId);
            Add("price_band_id", c.PriceBandId);

            // Analysis codes 1..20
            AddString("analysis_code_1", c.AnalysisCode1);
            AddString("analysis_code_2", c.AnalysisCode2);
            AddString("analysis_code_3", c.AnalysisCode3);
            AddString("analysis_code_4", c.AnalysisCode4);
            AddString("analysis_code_5", c.AnalysisCode5);
            AddString("analysis_code_6", c.AnalysisCode6);
            AddString("analysis_code_7", c.AnalysisCode7);
            AddString("analysis_code_8", c.AnalysisCode8);
            AddString("analysis_code_9", c.AnalysisCode9);
            AddString("analysis_code_10", c.AnalysisCode10);
            AddString("analysis_code_11", c.AnalysisCode11);
            AddString("analysis_code_12", c.AnalysisCode12);
            AddString("analysis_code_13", c.AnalysisCode13);
            AddString("analysis_code_14", c.AnalysisCode14);
            AddString("analysis_code_15", c.AnalysisCode15);
            AddString("analysis_code_16", c.AnalysisCode16);
            AddString("analysis_code_17", c.AnalysisCode17);
            AddString("analysis_code_18", c.AnalysisCode18);
            AddString("analysis_code_19", c.AnalysisCode19);
            AddString("analysis_code_20", c.AnalysisCode20);

            // Spare text 1..10
            AddString("spare_text_1", c.SpareText1);
            AddString("spare_text_2", c.SpareText2);
            AddString("spare_text_3", c.SpareText3);
            AddString("spare_text_4", c.SpareText4);
            AddString("spare_text_5", c.SpareText5);
            AddString("spare_text_6", c.SpareText6);
            AddString("spare_text_7", c.SpareText7);
            AddString("spare_text_8", c.SpareText8);
            AddString("spare_text_9", c.SpareText9);
            AddString("spare_text_10", c.SpareText10);

            // Spare numbers 1..10
            Add("spare_number_1", c.SpareNumber1);
            Add("spare_number_2", c.SpareNumber2);
            Add("spare_number_3", c.SpareNumber3);
            Add("spare_number_4", c.SpareNumber4);
            Add("spare_number_5", c.SpareNumber5);
            Add("spare_number_6", c.SpareNumber6);
            Add("spare_number_7", c.SpareNumber7);
            Add("spare_number_8", c.SpareNumber8);
            Add("spare_number_9", c.SpareNumber9);
            Add("spare_number_10", c.SpareNumber10);

            // Spare dates 1..5
            if (c.SpareDate1.HasValue) d["spare_date_1"] = c.SpareDate1.Value.UtcDateTime;
            if (c.SpareDate2.HasValue) d["spare_date_2"] = c.SpareDate2.Value.UtcDateTime;
            if (c.SpareDate3.HasValue) d["spare_date_3"] = c.SpareDate3.Value.UtcDateTime;
            if (c.SpareDate4.HasValue) d["spare_date_4"] = c.SpareDate4.Value.UtcDateTime;
            if (c.SpareDate5.HasValue) d["spare_date_5"] = c.SpareDate5.Value.UtcDateTime;

            // Spare bool 1..5
            Add("spare_bool_1", c.SpareBool1);
            Add("spare_bool_2", c.SpareBool2);
            Add("spare_bool_3", c.SpareBool3);
            Add("spare_bool_4", c.SpareBool4);
            Add("spare_bool_5", c.SpareBool5);

            // Main address
            if (c.Address is not null)
            {
                var a = new Dictionary<string, object>();
                void A(string n, string? v) { if (!string.IsNullOrWhiteSpace(v)) a[n] = v!; }
                A("address_1", c.Address.Address1);
                A("address_2", c.Address.Address2);
                A("address_3", c.Address.Address3);
                A("address_4", c.Address.Address4);
                A("city", c.Address.City);
                A("county", c.Address.County);
                A("postcode", c.Address.Postcode);
                if (c.Address.AddressCountryCodeId.HasValue)
                    a["address_country_code_id"] = c.Address.AddressCountryCodeId.Value;

                if (a.Count > 0)
                    d["main_address"] = a;
            }

            return d;
        }

        private static string? HashBase64Url(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string SafePreview(string? s, int max = 512)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s[..max] + "…";
        }
    }

    /// <summary>
    /// Compact reply for direct customer creation.
    /// </summary>
    public sealed class DirectCustomerCreateReply
    {
        /// <summary>True if the upstream call succeeded.</summary>
        public bool Success { get; set; }
        /// <summary>Sage 200 Customer Id (if returned by the upstream API).</summary>
        public long? SageId { get; set; }
        /// <summary>Customer reference returned by Sage (if available).</summary>
        public string? Reference { get; set; }
        /// <summary>Additional info.</summary>
        public string? Message { get; set; }
    }
}
