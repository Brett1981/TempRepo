// =========================================================================================================
/* SalesReceiptsService.cs — COMPLETE SERVICE
 * Responsibilities:
 *  1) Build upstream body in snake_case, omitting all null/empty members (standing rule).
 *  2) Forward X-Site/X-Company, propagate or derive Idempotency-Key (payload hash).
 *  3) POST /sales_receipts, parse URN, map typed failures.
 *  4) Optional Kafka publish via IEventPublisher (fire-and-forget on success).
 */
// =========================================================================================================

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sales;
using Sage200Microservice.Services.Messaging; // optional Kafka seam
using System.Text.Json;

namespace Sage200Microservice.Services.Implementations.Sales
{
    /// <summary>
    /// Posts Sales Receipts to Sage 200 (POST /sales_receipts).
    /// </summary>
    public sealed class SalesReceiptsService : ISalesReceiptsService
    {
        private readonly ISageApiClient _sage;
        private readonly ILogger<SalesReceiptsService> _log;
        private readonly IEventPublisher? _events; // optional Kafka

        public SalesReceiptsService(
            ISageApiClient sage,
            ILogger<SalesReceiptsService> log,
            IEventPublisher? events = null)
        {
            _sage = sage;
            _log = log;
            _events = events;
        }

        /// <inheritdoc />
        public async Task<SalesCreateResult> CreateAsync(SalesReceiptCreate request, HttpContext http, CancellationToken ct)
        {
            // ---------- Header guard ----------
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

            // ---------- Body (omit-nulls) ----------
            var body = BuildReceiptBody(request);

            // ---------- Idempotency key ----------
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Site"] = xSite.ToString(),
                ["X-Company"] = xCompany.ToString(),
            };

            if (http.Request.Headers.TryGetValue("Idempotency-Key", out var idem) && !StringValues.IsNullOrEmpty(idem))
            {
                headers["Idempotency-Key"] = idem.ToString();
            }
            else
            {
                var canon = JsonSerializer.Serialize(body);
                headers["Idempotency-Key"] = HashBase64Url(canon) ?? Guid.NewGuid().ToString("N");
            }

            // ---------- POST upstream ----------
            var (status, bodyText) = await _sage.PostForBodyAsync("sales_receipts", body, headers, ct);

            if (status is < 200 or > 299)
            {
                _log.LogWarning("Upstream error creating SalesReceipt: status={Status} body={Body}", status, SafePreview(bodyText));
                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Upstream error",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = status,
                    UpstreamBody = SafePreview(bodyText)
                };
            }

            // ---------- Parse URN ----------
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

            _log.LogInformation("sales_receipt created with URN={Urn}", urn);

            // ---------- Optional Kafka publish ----------
            try
            {
                if (_events is not null)
                {
                    await _events.PublishAsync("sales.receipt.created", new
                    {
                        urn,
                        customer_id = request.CustomerId,
                        bank_id = request.BankId,
                        cheque_value = request.ChequeValue,
                        reference = request.Reference
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Kafka publish failed for SalesReceipt URN={Urn}", urn);
            }

            return new SalesCreateResult { Success = true, Urn = urn, Message = "Created" };
        }

        // ----------------------- Helpers -----------------------

        private static Dictionary<string, object> BuildReceiptBody(SalesReceiptCreate req)
        {
            var body = new Dictionary<string, object>();

            void AddString(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) body[name] = value!;
            }

            // Required
            body["customer_id"] = req.CustomerId;
            body["bank_id"] = req.BankId;
            body["cheque_value"] = req.ChequeValue;

            // Optionals (omit when null/empty)
            if (req.ChequeCurrencyId.HasValue) body["cheque_currency_id"] = req.ChequeCurrencyId.Value;
            if (req.CustomerChequeValue.HasValue) body["customer_cheque_value"] = req.CustomerChequeValue.Value;
            if (req.TransactionDate.HasValue) body["transaction_date"] = req.TransactionDate.Value.UtcDateTime;
            if (req.ExchangeRate.HasValue) body["exchange_rate"] = req.ExchangeRate.Value;
            if (req.BankExchangeRate.HasValue) body["bank_exchange_rate"] = req.BankExchangeRate.Value;
            if (req.ChequeExchangeRate.HasValue) body["cheque_exchange_rate"] = req.ChequeExchangeRate.Value;
            if (req.SettlementDiscountValue.HasValue) body["settlement_discount_value"] = req.SettlementDiscountValue.Value;

            AddString("reference", req.Reference);
            AddString("second_reference", req.SecondReference);

            if (req.NominalAnalysisItems is { Count: > 0 })
            {
                var arr = new List<Dictionary<string, object>>();
                foreach (var n in req.NominalAnalysisItems)
                {
                    var d = new Dictionary<string, object> { ["code"] = n.Code };
                    if (!string.IsNullOrWhiteSpace(n.CostCentre)) d["cost_centre"] = n.CostCentre!;
                    if (!string.IsNullOrWhiteSpace(n.Department)) d["department"] = n.Department!;
                    if (!string.IsNullOrWhiteSpace(n.Narrative)) d["narrative"] = n.Narrative!;
                    if (n.Value.HasValue) d["value"] = n.Value.Value;
                    if (!string.IsNullOrWhiteSpace(n.TransactionAnalysisCode))
                        d["transaction_analysis_code"] = n.TransactionAnalysisCode!;
                    arr.Add(d);
                }
                if (arr.Count > 0) body["nominal_analysis_items"] = arr;
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
