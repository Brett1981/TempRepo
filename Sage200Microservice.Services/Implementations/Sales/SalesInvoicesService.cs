using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sage;
using Sage200Microservice.Services.Models.Sales;
using System.Text.Json;

namespace Sage200Microservice.Services.Implementations.Sales
{
    /// <summary>
    /// Service for creating Sales Invoices in Sage (canonical identifier = URN).
    /// Refactored to use strongly-typed Sage DTOs and System.Text.Json serialization.
    /// </summary>
    public sealed class SalesInvoicesService : ISalesInvoicesService
    {
        private readonly ILogger<SalesInvoicesService> _log;
        private readonly ISageApiClient _sage;
        private readonly SageApiSettings _cfg;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Constructs the service.
        /// </summary>
        public SalesInvoicesService(
            ILogger<SalesInvoicesService> log,
            ISageApiClient sage,
            IOptions<SageApiSettings> cfg,
            IOptions<JsonSerializerOptions> jsonOptions)
        {
            _log = log;
            _sage = sage;
            _cfg = cfg.Value ?? throw new ArgumentNullException(nameof(cfg));
            // Use injected options, ensuring snake_case and ignore nulls
            _jsonOptions = new JsonSerializerOptions(jsonOptions?.Value ?? new JsonSerializerOptions())
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// Create a Sales Invoice via Sage’s <c>sales_invoices</c> endpoint using Sage DTO and JsonSerializer.
        /// </summary>
        public async Task<SalesCreateResult> CreateAsync(SalesInvoiceCreate request, RequestContext context, CancellationToken ct)
        {
            // 1) --- STAGE 10 Change: Map internal model to Sage DTO ---
            var sageInvoiceDto = MapToSageSalesInvoice(request);

            // 2) --- STAGE 10 Change: Serialize Sage DTO ---
            string payloadJson;
            try
            {
                payloadJson = JsonSerializer.Serialize(sageInvoiceDto, _jsonOptions);
                _log.LogTrace("Sage SalesInvoice Payload (CorrelationId: {CorrelationId}): {Payload}", context.CorrelationId, payloadJson);
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "Failed to serialize SageSalesInvoice DTO. CorrelationId: {CorrelationId}", context.CorrelationId);
                return new SalesCreateResult { Success = false, Message = "Internal serialization error.", Failure = FailureKind.Validation }; // Or appropriate failure kind
            }


            // 3) Prepare Headers from RequestContext
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[_cfg.SiteHeaderName ?? "X-Site"] = context.SiteId; // Guaranteed non-empty by controller
            headers[_cfg.CompanyHeaderName ?? "X-Company"] = context.CompanyId; // Guaranteed non-empty by controller
            if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
            {
                headers["Idempotency-Key"] = context.IdempotencyKey;
            }
            // If Idempotency key is required but not provided, consider generating one based on payload hash here
            // else if (_cfg.RequireIdempotencyKeyForInvoices) { // Example config check
            //     headers["Idempotency-Key"] = HashBase64Url(payloadJson) ?? Guid.NewGuid().ToString("N");
            // }

            _log.LogDebug("CorrelationId: {CorrelationId}. SalesInvoice POST forwarding headers: X-Site={HasSite}, X-Company={HasCompany}, Idempotency-Key={HasIdem}",
                context.CorrelationId,
                headers.ContainsKey(_cfg.SiteHeaderName ?? "X-Site"),
                headers.ContainsKey(_cfg.CompanyHeaderName ?? "X-Company"),
                headers.ContainsKey("Idempotency-Key"));

            // 4) POST to Sage using ISageApiClient
            (int StatusCode, string Body) response;
            try
            {
                response = await _sage.PostForBodyAsync(
                    "sales_invoices",
                    payloadJson, // Pass the serialized string directly
                    headers,
                    ct);
            }
            catch (HttpRequestException httpEx) // Catch potential exceptions from API client itself
            {
                _log.LogError(httpEx, "Error calling Sage API during SalesInvoice creation. CorrelationId: {CorrelationId}", context.CorrelationId);
                return new SalesCreateResult
                {
                    Success = false,
                    Message = $"Sage API communication error: {httpEx.Message}",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = (int?)httpEx.StatusCode,
                    UpstreamBody = httpEx.Message
                };
            }
            catch (Exception ex) // Catch other unexpected errors
            {
                _log.LogError(ex, "Unexpected error during Sage API call for SalesInvoice creation. CorrelationId: {CorrelationId}", context.CorrelationId);
                return new SalesCreateResult { Success = false, Message = $"Internal error during API call: {ex.Message}", Failure = FailureKind.Upstream };
            }


            // 5) Handle Response
            if (response.StatusCode < 200 || response.StatusCode > 299)
            {
                _log.LogWarning("Upstream error creating SalesInvoice: status={Status} body={BodyPreview}. CorrelationId: {CorrelationId}",
                    response.StatusCode, SafePreview(response.Body, 512), context.CorrelationId);

                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Upstream error from Sage.",
                    Failure = response.StatusCode == 400 ? FailureKind.BadRequest : FailureKind.Upstream,
                    UpstreamStatusCode = response.StatusCode,
                    UpstreamBody = SafePreview(response.Body, 512) // Provide preview for diagnostics
                };
            }

            // 6) Extract URN from successful response
            string? urn = null;
            try
            {
                // Deserialize response to minimal URN DTO
                var urnResponse = JsonSerializer.Deserialize<SageUrnResponse>(response.Body, _jsonOptions);
                urn = urnResponse?.Urn;

                if (string.IsNullOrWhiteSpace(urn))
                {
                    _log.LogWarning("Sage SalesInvoice created (Status {Status}) but response did not contain a 'urn'. Body: {BodyPreview}. CorrelationId: {CorrelationId}",
                       response.StatusCode, SafePreview(response.Body, 512), context.CorrelationId);
                    // Decide if this is critical - returning success but flagging missing URN
                    return new SalesCreateResult { Success = true, Urn = null, Message = "Created in Sage, but URN was missing in response." };
                }
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "Failed to deserialize Sage SalesInvoice creation response (Status {Status}). Body: {BodyPreview}. CorrelationId: {CorrelationId}",
                    response.StatusCode, SafePreview(response.Body, 512), context.CorrelationId);
                return new SalesCreateResult
                {
                    Success = false,
                    Message = "Failed to parse successful Sage response.",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = response.StatusCode,
                    UpstreamBody = SafePreview(response.Body, 512)
                };
            }

            _log.LogInformation("SalesInvoice created successfully with URN={Urn}. CorrelationId: {CorrelationId}", urn, context.CorrelationId);

            return new SalesCreateResult { Success = true, Urn = urn, Message = "Created" };
        }

        public async Task<SalesCreateResult> CreateInvoiceFromSopAsync(string sopOrderUrn, RequestContext context, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sopOrderUrn))
                return new SalesCreateResult { Success = false, Message = "Missing SOP URN." };

            try
            {
                var body = JsonSerializer.Serialize(new { sop_order_urn = sopOrderUrn });
                var headers = new Dictionary<string, string>
                {
                    ["X-Site"] = context.SiteId ?? string.Empty,
                    ["X-Company"] = context.CompanyId ?? string.Empty
                };

                // Your API client returns (int StatusCode, string Body)
                var response = await _sage.PostForBodyAsync("sales_invoices/from_sop_order", body, headers, ct);

                if (response.StatusCode is >= 200 and < 300)
                {
                    var result = JsonSerializer.Deserialize<SageUrnResponse>(response.Body, _jsonOptions);
                    return new SalesCreateResult
                    {
                        Success = true,
                        Urn = result?.Urn,
                        Message = "Invoice created from SOP."
                    };
                }

                return new SalesCreateResult
                {
                    Success = false,
                    Message = $"Failed to create invoice from SOP. Upstream status {response.StatusCode}",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = response.StatusCode,
                    UpstreamBody = response.Body
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating invoice from SOP URN {Urn}.", sopOrderUrn);
                return new SalesCreateResult
                {
                    Success = false,
                    Message = ex.Message,
                    Failure = FailureKind.Upstream
                };
            }
        }


        /// <summary>
        /// [NEW ADDITION - STAGE 10]
        /// Maps the internal SalesInvoiceCreate model to the SageSalesInvoice DTO for serialization.
        /// </summary>
        private static SageSalesInvoice MapToSageSalesInvoice(SalesInvoiceCreate r)
        {
            var sageDto = new SageSalesInvoice
            {
                CustomerId = r.CustomerId,
                TransactionDate = r.TransactionDate,
                DueDate = r.DueDate,
                ExchangeRate = r.ExchangeRate,
                SettledImmediately = r.SettledImmediately,
                DocumentGoodsValue = r.DocumentGoodsValue,
                DocumentTaxValue = r.DocumentTaxValue,
                DocumentDiscountValue = r.DocumentDiscountValue,
                DocumentTaxDiscountValue = r.DocumentTaxDiscountValue,
                DiscountPercent = r.DiscountPercent,
                DiscountDays = r.DiscountDays,
                TriangularTransaction = r.TriangularTransaction,
                Reference = r.Reference,
                SecondReference = r.SecondReference,

                TaxAnalysisItems = r.TaxAnalysisItems?.Select(item => new SageTaxAnalysisItem
                {
                    Id = item.Id,
                    GoodsAmount = item.GoodsAmount,
                    DiscountAmount = item.DiscountAmount,
                    TaxAmount = item.TaxAmount,
                    TaxDiscountAmount = item.TaxDiscountAmount
                }).ToList(),

                NominalAnalysisItems = r.NominalAnalysisItems?.Select(item => new SageNominalAnalysisItem
                {
                    Code = item.Code,
                    CostCentre = item.CostCentre,
                    Department = item.Department,
                    Narrative = item.Narrative,
                    Value = item.Value,
                    TransactionAnalysisCode = item.TransactionAnalysisCode
                }).ToList()
            };

            return sageDto;
        }

        // --- Removed BuildInvoiceBody helper ---

        /// <summary>
        /// Returns a short, safe excerpt of upstream text.
        /// </summary>
        private static string SafePreview(string? s, int max = 512)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // AddHeaderIfNotEmpty helper removed as headers are passed directly to API client method
    }
}
