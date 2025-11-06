/* =========================================================================================================
 * SalesCreditNotesService.cs  —  COMPLETE SERVICE (aligned to Sales_Invoices)
 *
 * Responsibilities:
 *  1) Build an upstream body that omits all null/empty members (never send explicit nulls).  [Standing Rule]
 *  2) Forward X-Site/X-Company to Sage and propagate/derive Idempotency-Key.
 *  3) POST to /sales_credit_notes and parse URN.
 *  4) Typed failure mapping for controllers.
 *  5) Hook: publish Kafka event on success (optional; interface shown).
 *
 * Dependencies:
 *  - ISageApiClient _sage : low-level HTTP client already used elsewhere in the solution.
 *    We use _sage.PostForBodyAsync("sales_credit_notes", body, headers, ct) → (status, bodyText).
 *  - ILogger<SalesCreditNotesService> _log
 *  - (Optional) IEventPublisher _events : for Kafka integration (fire-and-forget on success).
 * ========================================================================================================= */

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sales;
using System.Text.Json;

namespace Sage200Microservice.Services.Implementations.Sales
{
    public sealed class SalesCreditNotesService : ISalesCreditNotesService
    {
        private readonly ISageApiClient _sage;
        private readonly ILogger<SalesCreditNotesService> _log;
        private readonly IEventPublisher? _events; // optional Kafka

        public SalesCreditNotesService(
            ISageApiClient sage,
            ILogger<SalesCreditNotesService> log,
            IEventPublisher? events = null)
        {
            _sage = sage;
            _log = log;
            _events = events;
        }

        /// <summary>
        /// Create a Sales Credit Note in Sage 200 (POST /sales_credit_notes).
        /// </summary>
        public async Task<SalesCreateResult> CreateAsync(SalesCreditNoteCreate request, HttpContext http, CancellationToken ct)
        {
            // ------------------------------
            // 1) Validate required headers
            // ------------------------------
            if (!http.Request.Headers.TryGetValue("X-Site", out StringValues xSite) || StringValues.IsNullOrEmpty(xSite) ||
                !http.Request.Headers.TryGetValue("X-Company", out StringValues xCompany) || StringValues.IsNullOrEmpty(xCompany))
            {
                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Missing X-Site and/or X-Company headers.",
                    Failure = FailureKind.BadRequest
                };
            }

            // ------------------------------
            // 2) Omit-null JSON body build
            // ------------------------------
            var body = BuildCreditNoteBody(request);

            // ------------------------------
            // 3) Idempotency-Key logic
            // ------------------------------
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Site"] = xSite.ToString(),
                ["X-Company"] = xCompany.ToString()
            };

            if (http.Request.Headers.TryGetValue("Idempotency-Key", out var idem) && !StringValues.IsNullOrEmpty(idem))
            {
                headers["Idempotency-Key"] = idem.ToString();
            }
            else
            {
                // Deterministic key based on the canonical JSON payload (stable ordering).
                var canon = JsonSerializer.Serialize(body);
                headers["Idempotency-Key"] = HashBase64Url(canon) ?? Guid.NewGuid().ToString("N");
            }

            // ------------------------------
            // 4) POST upstream
            // ------------------------------
            var (status, bodyText) = await _sage.PostForBodyAsync("sales_credit_notes", body, headers, ct);

            if (status < 200 || status > 299)
            {
                _log.LogWarning("Upstream error creating Sales Credit Note: status={Status} body={BodyPreview}",
                    status, SafePreview(bodyText));
                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Upstream error",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = status,
                    UpstreamBody = SafePreview(bodyText)
                };
            }

            // ------------------------------
            // 5) Parse URN
            // ------------------------------
            string? urn = null;
            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                if (doc.RootElement.TryGetProperty("urn", out var urnProp) && urnProp.ValueKind != JsonValueKind.Null)
                    urn = urnProp.ValueKind == JsonValueKind.String ? urnProp.GetString()
                         : urnProp.ValueKind == JsonValueKind.Number ? urnProp.GetRawText() : null;
            }
            catch (JsonException)
            {
                // some gateways return plain text on success
            }

            if (string.IsNullOrWhiteSpace(urn))
            {
                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Sage did not return URN.",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = status,
                    UpstreamBody = SafePreview(bodyText)
                };
            }

            _log.LogInformation("sales_credit_note created with URN={Urn}", urn);

            // ------------------------------
            // 6) Optional Kafka publish
            // ------------------------------
            try
            {
                if (_events is not null)
                {
                    await _events.PublishAsync("sales.creditnote.created", new
                    {
                        urn,
                        customer_id = request.CustomerId,
                        reference = request.Reference,
                        document_goods_value = request.DocumentGoodsValue,
                        document_tax_value = request.DocumentTaxValue
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal; do not fail user operation on event bus issues
                _log.LogWarning(ex, "Kafka publish failed for credit note URN={Urn}", urn);
            }

            return new SalesCreateResult { Success = true, Urn = urn, Message = "Created" };
        }

        // ----------------------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------------------

        private static Dictionary<string, object> BuildCreditNoteBody(SalesCreditNoteCreate req)
        {
            // ALWAYS omit nulls / empties
            var body = new Dictionary<string, object>();

            void Add<T>(string name, T? value)
            {
                if (value is null) return;
                body[name] = value!;
            }
            void AddString(string name, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                body[name] = value;
            }

            // Header fields
            body["customer_id"] = req.CustomerId; // required
            Add("transaction_date", req.TransactionDate?.UtcDateTime);
            Add("exchange_rate", req.ExchangeRate);
            AddString("reference", req.Reference);
            AddString("second_reference", req.SecondReference);
            Add("settled_immediately", req.SettledImmediately);
            Add("document_goods_value", req.DocumentGoodsValue);
            Add("document_tax_value", req.DocumentTaxValue);
            Add("document_discount_value", req.DocumentDiscountValue);
            Add("document_tax_discount_value", req.DocumentTaxDiscountValue);
            Add("discount_percent", req.DiscountPercent);
            Add("discount_days", req.DiscountDays);
            Add("triangular_transaction", req.TriangularTransaction);

            // Tax analysis (requires id; other amounts optional)
            if (req.TaxAnalysisItems is { Count: > 0 })
            {
                var items = new List<Dictionary<string, object>>();
                foreach (var t in req.TaxAnalysisItems)
                {
                    var d = new Dictionary<string, object> { ["id"] = t.Id };
                    if (t.GoodsAmount.HasValue) d["goods_amount"] = t.GoodsAmount.Value;
                    if (t.DiscountAmount.HasValue) d["discount_amount"] = t.DiscountAmount.Value;
                    if (t.TaxAmount.HasValue) d["tax_amount"] = t.TaxAmount.Value;
                    if (t.TaxDiscountAmount.HasValue) d["tax_discount_amount"] = t.TaxDiscountAmount.Value;
                    items.Add(d);
                }
                if (items.Count > 0) body["tax_analysis_items"] = items;
            }

            // Nominal analysis (requires code)
            if (req.NominalAnalysisItems is { Count: > 0 })
            {
                var items = new List<Dictionary<string, object>>();
                foreach (var n in req.NominalAnalysisItems)
                {
                    var d = new Dictionary<string, object> { ["code"] = n.Code };
                    if (!string.IsNullOrWhiteSpace(n.CostCentre)) d["cost_centre"] = n.CostCentre!;
                    if (!string.IsNullOrWhiteSpace(n.Department)) d["department"] = n.Department!;
                    if (!string.IsNullOrWhiteSpace(n.Narrative)) d["narrative"] = n.Narrative!;
                    if (n.Value.HasValue) d["value"] = n.Value.Value;
                    if (!string.IsNullOrWhiteSpace(n.TransactionAnalysisCode))
                        d["transaction_analysis_code"] = n.TransactionAnalysisCode!;
                    items.Add(d);
                }
                if (items.Count > 0) body["nominal_analysis_items"] = items;
            }

            return body;
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
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}

/* ----------------------------------------------------------------------------------------------------------
 * OPTIONAL KAFKA PUBLISHER ABSTRACTION (drop in the Events project if we want to enable eventing)
 * ----------------------------------------------------------------------------------------------------------
namespace Sage200Microservice.Services.Models
{
    public interface IEventPublisher
    {
        Task PublishAsync(string topic, object payload, CancellationToken ct = default);
    }
}
*/
