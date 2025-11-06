using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Models; // For IdempotencyRecord
using Sage200Microservice.Data.Repositories; // For IIdempotencyRecordRepository
using Sage200Microservice.Services.Configuration; // For SopFeaturesOptions
using Sage200Microservice.Services.Interfaces; // For ISopOrderService, ISageApiClient etc.
using Sage200Microservice.Services.Messaging; // For IEventPublisher
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models; // For RequestContext, PagedResult etc.
using Sage200Microservice.Services.Models.Sage; // For Sage DTOs
using Sage200Microservice.Services.Models.Sales; // For FailureKind
using Sage200Microservice.Services.Models.Sop; // For SopOrderCreate, SopOrderDto etc.
using Sage200Microservice.Services.Shared; // For OData helpers
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http; // For HttpRequestException
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // For JsonPropertyName
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Implementations.Sop
{
    /// <summary>
    /// [REPLACEMENT - STAGE 10]
    /// Default implementation for SOP order operations. Relies on ISageApiClient.
    /// Refactored to use strongly-typed Sage DTOs and System.Text.Json serialization.
    /// </summary>
    public sealed partial class SopOrderService : ISopOrderService
    {
        private readonly ISageApiClient _api;
        private readonly ILogger<SopOrderService> _log;
        private readonly IIdempotencyRecordRepository _idem;
        private readonly IEventPublisher _events;
        private readonly IOptionsMonitor<SopFeaturesOptions> _sopFeatures;
        private readonly JsonSerializerOptions _jsonOptions; // Added for serialization

        /// <summary>
        /// Constructor updated to inject JsonSerializerOptions.
        /// </summary>
        public SopOrderService(
            ISageApiClient api,
            ILogger<SopOrderService> log,
            IIdempotencyRecordRepository idem,
            IEventPublisher events,
            IOptionsMonitor<SopFeaturesOptions> sopFeatures,
            IOptions<JsonSerializerOptions> jsonOptions) // Injected options
        {
            _api = api;
            _log = log;
            _idem = idem;
            _events = events;
            _sopFeatures = sopFeatures;

            // Use injected options, ensuring snake_case and ignore nulls are set
            _jsonOptions = new JsonSerializerOptions(jsonOptions?.Value ?? new JsonSerializerOptions())
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                // Ensure DateTime handling is correct if not globally configured
            };
        }

        /// <summary>
        /// Lists SOP Orders using strongly-typed Sage DTOs.
        /// Builds a single OData URL and calls Sage. If the upstream returns 502 for
        /// complex filters, we try a status-chunk fallback.
        /// </summary>
        public async Task<PagedResult<SopOrderDto>> ListAsync(SopOrderQuery query, HttpContext http, CancellationToken ct)
        {
            // 1) Compose friendly filter onto any existing OData filter (logic unchanged)
            var composedFilter = FriendlyFilters.Compose(
                existingFilter: query.ODataFilter,
                customerId: query.CustomerId,
                orderNo: query.OrderNo,
                status: query.Status,
                fromDate: query.FromDate,
                toDate: query.ToDate);

            // 2) Apply status logic (logic unchanged)
            if (query.StatusWhitelist is { Count: > 0 })
            {
                composedFilter = FriendlyFilters.RemoveDocumentStatus(composedFilter);
                var or = FriendlyFilters.BuildEnumWhitelist(query.StatusWhitelist);
                if (!string.IsNullOrWhiteSpace(or))
                    composedFilter = string.IsNullOrWhiteSpace(composedFilter) ? $"({or})" : $"({composedFilter}) and ({or})";
            }
            else
            {
                composedFilter = FriendlyFilters.AppendOutstandingWhitelist(composedFilter);
            }

            // 3) $select to reduce payload surface (logic unchanged)
            const string select =
                "id,document_no,reference,customer_id,customer_reference,document_status,status,order_date,promised_date," + // Added reference
                "currency_code,subtotal_goods_value,total_tax_value,total_gross_value,spare_text_1,analysis_code_1"; // Simplified totals, use header fields

            var includeCount = query.IncludeCount ?? true;
            var extra = new Dictionary<string, string?>
            {
                ["$select"] = select,
                ["$count"] = includeCount ? "true" : "false" // Use string "true"/"false" for OData bool
            };

            // 4) Build URL (logic unchanged)
            var url = OData.BuildUrl(
                basePath: "sop_orders",
                filter: composedFilter,
                orderBy: query.OrderBy,
                top: query.Top,
                skip: query.Skip,
                extra: extra);

            _log.LogInformation("SOP list query URL => {ODataUrl}", url);

            try
            {
                // --- STAGE 10 Change: Deserialize directly to ODataResponse<SageSopOrder> ---
                var response = await _api.GetAsync<ODataResponse<SageSopOrder>>(url, ct);
                List<SageSopOrder> items = response?.Value ?? new List<SageSopOrder>();
                int total = response?.ODataCount ?? items.Count; // Use OData count if available

                // --- STAGE 10 Change: Map from SageSopOrder to SopOrderDto ---
                var dtos = items.Select(MapHeader).ToList(); // MapHeader now takes SageSopOrder
                return new PagedResult<SopOrderDto>(dtos, total, query.Skip ?? 0, query.Top ?? 50); // Use correct PagedResult constructor
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadGateway)
            {
                _log.LogWarning(ex, "Upstream 502 for {Url}. Attempting status-chunk fallback without $count.", url);

                IReadOnlyList<int>? codes =
                    (query.StatusWhitelist is { Count: > 0 }) ? query.StatusWhitelist :
                    (FriendlyFilters.ContainsOutstandingEnumBlock(composedFilter) ? FriendlyFilters.OutstandingCodes : null);

                if (codes is null || codes.Count == 0)
                {
                    _log.LogError("Cannot perform status-chunk fallback for 502 as no status codes could be determined from the filter: {Filter}", composedFilter);
                    throw; // Rethrow if no sensible fallback possible
                }

                var baseFilterNoStatus = FriendlyFilters.RemoveDocumentStatus(composedFilter);

                // --- STAGE 10 Change: Fallback uses updated FetchByStatusChunksAsync ---
                (List<SageSopOrder> merged, int totalApprox) = await FetchByStatusChunksAsync(
                    baseComposedFilter: baseFilterNoStatus,
                    orderBy: query.OrderBy,
                    pageSize: query.Top ?? 50,
                    skip: query.Skip ?? 0,
                    codes: codes,
                    ct: ct);

                var dtos = merged.Select(MapHeader).ToList();
                return new PagedResult<SopOrderDto>(dtos, totalApprox, query.Skip ?? 0, query.Top ?? 50); // Use approximated total
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error listing SOP Orders with URL: {Url}", url);
                throw;
            }
        }

        public Task<SopOrderCreateResult> CreateSopOrderAsync(SopOrderCreate request, HttpContext http, CancellationToken ct = default)
    => CreateAsync(request, http, ct);

        /// <summary>
        /// [REPLACE EXISTING] Fetches items by status code chunks using strongly-typed DTOs.
        /// </summary>
        private async Task<(List<SageSopOrder> items, int total)> FetchByStatusChunksAsync(
            string? baseComposedFilter,
            string? orderBy,
            int pageSize,
            int skip,
            IReadOnlyList<int> codes,
            CancellationToken ct)
        {
            var collected = new List<SageSopOrder>(capacity: pageSize);
            var remainingToSkip = Math.Max(0, skip);
            var remainingToTake = Math.Max(1, pageSize);
            int totalApproxFromChunks = 0; // Approximate total by summing successful chunk counts

            // --- STAGE 10 Change: Use same select as primary ListAsync ---
            const string select =
                "id,document_no,reference,customer_id,customer_reference,document_status,status,order_date,promised_date," +
                "currency_code,subtotal_goods_value,total_tax_value,total_gross_value,spare_text_1,analysis_code_1";

            foreach (var c in codes.OrderBy(code => code)) // Process codes consistently
            {
                if (remainingToTake == 0) break; // Stop if page is full
                ct.ThrowIfCancellationRequested();

                string enumEq;
                try { enumEq = FriendlyFilters.BuildEnumEquality(c); }
                catch (KeyNotFoundException)
                {
                    _log.LogWarning("Skipping unknown document_status code {Code} in chunk fallback.", c);
                    continue;
                }

                var filterForCode = string.IsNullOrWhiteSpace(baseComposedFilter)
                    ? enumEq
                    : $"({baseComposedFilter}) and ({enumEq})";

                // Build URL WITHOUT $count, but WITH $select
                var extra = new Dictionary<string, string?>
                {
                    ["$count"] = "false", // Explicitly disable count for fallback
                    ["$select"] = select
                };

                // Fetch enough to potentially cover skip + take for this chunk
                var url = OData.BuildUrl("sop_orders", filterForCode, orderBy, top: remainingToSkip + remainingToTake, skip: 0, extra: extra);
                _log.LogDebug("Fetching chunk for status {StatusCode}: {Url}", c, url);

                ODataResponse<SageSopOrder>? response;
                try
                {
                    // --- STAGE 10 Change: Deserialize directly ---
                    response = await _api.GetAsync<ODataResponse<SageSopOrder>>(url, ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadGateway)
                {
                    _log.LogWarning(ex, "502 fetching SOP orders chunk for document_status={Code}. Skipping this code.", c);
                    continue; // Skip this chunk on error
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error fetching SOP orders chunk for document_status={Code}. Skipping this code.", c);
                    continue; // Skip this chunk on other errors
                }

                List<SageSopOrder> itemsForCode = response?.Value ?? new List<SageSopOrder>();
                totalApproxFromChunks += itemsForCode.Count; // Add count from this chunk to approximation

                // Apply client-side skip/take across merged set
                foreach (var it in itemsForCode)
                {
                    if (remainingToSkip > 0)
                    {
                        remainingToSkip--;
                        continue;
                    }
                    if (remainingToTake == 0) break; // Should not happen if top was calculated correctly, but defensive

                    collected.Add(it);
                    remainingToTake--;
                }
            }

            // If we didn't fill the page, the approximation might be the actual total
            // If we did fill the page, the approximation is likely lower than the real total
            _log.LogInformation("Fallback fetch collected {CollectedCount} items after skipping {SkippedCount}. Approximated total across fetched chunks: {TotalApprox}",
                 collected.Count, skip, totalApproxFromChunks);

            return (collected, totalApproxFromChunks); // Return approximation
        }

        /// <summary>
        /// [REPLACE EXISTING] Maps a SageSopOrder DTO to our internal SopOrderDto.
        /// </summary>
        private static SopOrderDto MapHeader(SageSopOrder s) => new SopOrderDto // Input type changed
        {
            Id = s.Id ?? 0, // Handle nullable ID
            // Prefer document_no, fallback to reference if document_no is empty (as per original logic)
            OrderNo = !string.IsNullOrWhiteSpace(s.DocumentNo) ? s.DocumentNo : s.Reference,
            CustomerId = s.CustomerId ?? 0, // Handle nullable CustomerId
            CustomerReference = s.CustomerReference,
            // Use existing Normalize logic, trying document_status first, then status
            Status = NormalizeDocumentStatus(s.DocumentStatus) ?? NormalizeDocumentStatus(s.Status),
            OrderDate = ToNullableUtc(s.DocumentDate), // Map from DocumentDate
            PromisedDate = ToNullableUtc(s.PromisedDeliveryDate), // Map from PromisedDeliveryDate
            CurrencyCode = s.CurrencyCode,
            // Map totals (handle nullables)
            NetTotal = s.SubtotalGoodsValue ?? 0m, // Map from SubtotalGoodsValue as NetTotal approximation
            TaxTotal = s.TotalTaxValue ?? 0m,
            GrossTotal = s.TotalGrossValue ?? 0m,
            // Patch 1 – External ID mapping (header)
            SourceExternalId = string.IsNullOrWhiteSpace(s.SpareText1) ? s.AnalysisCode1 : s.SpareText1
        };

        /// <summary>
        /// [REPLACE EXISTING] Gets a single order with lines using strongly-typed Sage DTOs.
        /// </summary>
        public async Task<SopOrderDto?> GetAsync(long id, HttpContext http, CancellationToken ct)
        {
            SageSopOrder? header;
            try
            {
                // --- STAGE 10 Change: Get strongly-typed header ---
                header = await _api.GetAsync<SageSopOrder>($"sop_orders({id})", ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogInformation("SOP order {Id} not found on Sage (404). Returning null.", id);
                return null;
            }
            // Allow other exceptions to propagate

            if (header?.Id == null) return null; // Defensive check

            // --- STAGE 10 Change: Get strongly-typed lines ---
            List<SageSopOrderLine> lines;
            try
            {
                // Fetch lines using ODataResponse helper DTO
                var linesResponse = await _api.GetAsync<ODataResponse<SageSopOrderLine>>($"sop_orders({id})/sop_order_lines", ct);
                lines = linesResponse?.Value ?? new List<SageSopOrderLine>();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogInformation("Lines for SOP order {Id} not found (404). Returning header only.", id);
                lines = new List<SageSopOrderLine>(); // Return header with empty lines
            }
            // Allow other exceptions to propagate


            // --- STAGE 10 Change: Map from Sage DTOs to internal SopOrderDto ---
            var dto = new SopOrderDto
            {
                Id = header.Id.Value,
                // Prefer document_no, fallback to reference
                OrderNo = !string.IsNullOrWhiteSpace(header.DocumentNo) ? header.DocumentNo : header.Reference,
                CustomerId = header.CustomerId ?? 0,
                CustomerReference = header.CustomerReference,
                Status = NormalizeDocumentStatus(header.DocumentStatus) ?? NormalizeDocumentStatus(header.Status),
                OrderDate = ToNullableUtc(header.DocumentDate),
                PromisedDate = ToNullableUtc(header.PromisedDeliveryDate),
                CurrencyCode = header.CurrencyCode,
                NetTotal = header.SubtotalGoodsValue ?? 0m, // Approximate Net
                TaxTotal = header.TotalTaxValue ?? 0m,
                GrossTotal = header.TotalGrossValue ?? 0m,
                SourceExternalId = string.IsNullOrWhiteSpace(header.SpareText1) ? header.AnalysisCode1 : header.SpareText1,
                Lines = lines.Select(l => new SopOrderLineDto
                {
                    OrderId = l.SopOrderId ?? 0, // Map from SopOrderId
                    LineNumber = l.LineNumber ?? 0, // Map from LineNumber
                    ProductCode = l.Code ?? "", // Map from Code
                    Description = l.Description ?? "",
                    Quantity = l.LineQuantity ?? 0m, // Map from LineQuantity
                    UnitPrice = l.SellingUnitPrice ?? 0m, // Map from SellingUnitPrice
                    NetTotal = l.LineTotalValue ?? 0m, // Map from LineTotalValue as Net approximation
                    TaxTotal = l.LineTaxValue ?? 0m, // Map from LineTaxValue
                    GrossTotal = (l.LineTotalValue ?? 0m) + (l.LineTaxValue ?? 0m), // Calculate Gross
                    SourceExternalLineId = string.IsNullOrWhiteSpace(l.SpareText1) ? l.AnalysisCode1 : l.SpareText1
                }).ToList()
            };

            return dto;
        }

        /// <summary>
        /// Creates a SOP order using Sage DTO and JsonSerializer, with idempotency and event publish.
        /// </summary>
        public async Task<SopOrderCreateResult> CreateAsync(SopOrderCreate request, HttpContext http, CancellationToken ct)
        {
            string correlationId = TryGetCorrelationId(http) ?? Guid.NewGuid().ToString();
            _log.LogInformation("Attempting to create SOP Order. CorrelationId: {CorrelationId}, CustomerId: {CustomerId}",
                 correlationId, request.Header.CustomerId);

            // 1) Idempotency by Idempotency-Key (if provided in header or body)
            string? idempotencyKey = request.IdempotencyKey ?? http.Request.Headers["Idempotency-Key"].ToString();
            string? keyHash = HashBase64Url(idempotencyKey); // Hash helper remains

            if (keyHash is not null)
            {
                IdempotencyRecord? existing = null;
                try
                {
                    existing = await _idem.GetByKeyHashAsync(keyHash, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error checking idempotency record for hash {KeyHash}. Proceeding with creation.", keyHash);
                    // Allow creation to proceed if idempotency check fails
                }

                if (existing is not null && existing.ResourceId.HasValue) // Check ResourceId for prior successful ID
                {
                    long priorId = existing.ResourceId.Value;
                    _log.LogInformation("Idempotent replay detected for key hash {KeyHash}; returning prior order id {OrderId}. CorrelationId: {CorrelationId}",
                        keyHash, priorId, correlationId);
                    // Consider fetching the OrderReference if needed for the result, or return null/empty
                    return new SopOrderCreateResult { Success = true, Message = "Idempotent replay", OrderId = priorId, OrderReference = null };
                }
                else if (existing is not null)
                {
                    // Key exists but no ResourceId - maybe failed previously or still in progress?
                    // Current behaviour: proceed with creation attempt. Alternative: return conflict/pending.
                    _log.LogWarning("Idempotency key hash {KeyHash} exists but has no ResourceId. Proceeding with creation attempt. CorrelationId: {CorrelationId}", keyHash, correlationId);
                }
            }

            // 2) --- STAGE 10 Change: Map internal model to Sage DTO ---
            SageSopOrder sageDto;
            try
            {
                sageDto = MapCreateToSageDto(request);
            }
            catch (Exception mapEx)
            {
                _log.LogError(mapEx, "Error mapping SopOrderCreate to SageSopOrder DTO. CorrelationId: {CorrelationId}", correlationId);
                return new SopOrderCreateResult { Success = false, Message = "Internal mapping error.", Failure = FailureKind.Validation };
            }

            // 3) --- STAGE 10 Change: Serialize Sage DTO ---
            string payloadJson;
            try
            {
                payloadJson = JsonSerializer.Serialize(sageDto, _jsonOptions);
                _log.LogTrace("Sage SOP Order Payload (CorrelationId: {CorrelationId}): {Payload}", correlationId, payloadJson);
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "Failed to serialize SageSopOrder DTO. CorrelationId: {CorrelationId}", correlationId);
                return new SopOrderCreateResult { Success = false, Message = "Internal serialization error.", Failure = FailureKind.Validation };
            }

            // 4) Prepare Headers
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Read from HttpContext - assume these are validated upstream or use defaults
            if (http.Request.Headers.TryGetValue("X-Site", out var xSite)) headers["X-Site"] = xSite.ToString();
            if (http.Request.Headers.TryGetValue("X-Company", out var xCompany)) headers["X-Company"] = xCompany.ToString();
            if (!string.IsNullOrWhiteSpace(idempotencyKey)) headers["Idempotency-Key"] = idempotencyKey;

            // 5) POST to Sage using ISageApiClient.PostForBodyAsync
            (int status, string bodyText) response;
            try
            {
                response = await _api.PostForBodyAsync("sop_orders", payloadJson, headers, ct);
            }
            catch (HttpRequestException httpEx)
            {
                _log.LogError(httpEx, "Error calling Sage API during SOP Order creation. CorrelationId: {CorrelationId}", correlationId);
                return new SopOrderCreateResult
                {
                    Success = false,
                    Message = $"Sage API communication error: {httpEx.Message}",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = (int?)httpEx.StatusCode,
                    UpstreamBody = httpEx.Message
                };
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("Sage API call for SOP Order creation timed out or was cancelled. CorrelationId: {CorrelationId}", correlationId);
                return new SopOrderCreateResult { Success = false, Message = "Sage API call timed out or was cancelled.", Failure = FailureKind.Upstream };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error during Sage API call for SOP Order creation. CorrelationId: {CorrelationId}", correlationId);
                return new SopOrderCreateResult { Success = false, Message = $"Internal error during API call: {ex.Message}", Failure = FailureKind.Upstream };
            }

            // 6) Handle Response Status
            if (response.status < 200 || response.status > 299)
            {
                _log.LogWarning("Upstream error creating SOP Order: status={Status} body={BodyPreview}. CorrelationId: {CorrelationId}",
                    response.status, SafePreview(response.bodyText, 512), correlationId);
                return new SopOrderCreateResult
                {
                    Success = false,
                    Message = "Upstream error from Sage.",
                    Failure = response.status == 400 ? FailureKind.BadRequest : FailureKind.Upstream,
                    UpstreamStatusCode = response.status,
                    UpstreamBody = SafePreview(response.bodyText, 512)
                };
            }

            // 7) --- STAGE 10 Change: Deserialize Response to SageSopOrder ---
            SageSopOrder? createdSageOrder = null;
            long? orderId = null;
            string? orderRef = null;
            try
            {
                createdSageOrder = JsonSerializer.Deserialize<SageSopOrder>(response.bodyText, _jsonOptions);
                orderId = createdSageOrder?.Id;
                // Use DocumentNo as the primary reference, fallback to Reference if needed
                orderRef = !string.IsNullOrWhiteSpace(createdSageOrder?.DocumentNo) ? createdSageOrder.DocumentNo : createdSageOrder?.Reference;

                if (!orderId.HasValue)
                {
                    _log.LogWarning("Sage SOP Order created (Status {Status}) but response did not contain an 'id'. Body: {BodyPreview}. CorrelationId: {CorrelationId}",
                       response.status, SafePreview(response.bodyText, 512), correlationId);
                    // Decide handling - return success but flag missing ID?
                    return new SopOrderCreateResult { Success = true, OrderId = null, OrderReference = orderRef, Message = "Created in Sage, but Order ID was missing in response." };
                }
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "Failed to deserialize Sage SOP Order creation response (Status {Status}). Body: {BodyPreview}. CorrelationId: {CorrelationId}",
                    response.status, SafePreview(response.bodyText, 512), correlationId);
                return new SopOrderCreateResult
                {
                    Success = false,
                    Message = "Failed to parse successful Sage response.",
                    Failure = FailureKind.Upstream,
                    UpstreamStatusCode = response.status,
                    UpstreamBody = SafePreview(response.bodyText, 512)
                };
            }

            _log.LogInformation("SOP Order created successfully with ID={OrderId}, Ref={OrderRef}. CorrelationId: {CorrelationId}",
                orderId, orderRef, correlationId);


            // 8) Persist idempotency record if key was used and creation succeeded
            if (keyHash is not null && orderId.HasValue)
            {
                var rec = new IdempotencyRecord
                {
                    KeyHash = keyHash,
                    RequestHash = HashBase64Url(payloadJson), // Hash the actual sent payload
                    ResourceId = orderId.Value, // Store the Sage Order ID
                    CreatedUtc = DateTime.UtcNow,
                    ExpiresUtc = DateTime.UtcNow.AddDays(7) // Or configurable retention
                };
                try
                {
                    await _idem.SaveAsync(rec, ct);
                    _log.LogDebug("Saved idempotency record for hash {KeyHash}, OrderId {OrderId}", keyHash, orderId.Value);
                }
                catch (Exception idemEx)
                {
                    // Log but don't fail the overall success response
                    _log.LogError(idemEx, "Failed to save idempotency record for hash {KeyHash}, OrderId {OrderId}. Order was created successfully.", keyHash, orderId.Value);
                }
            }

            // 9) Publish event (logic unchanged)
            if (_sopFeatures.CurrentValue.PublishCreatedEventEnabled)
            {
                var tenantId = TryGetTenantId(http.User);
                try
                {
                    _events.Publish("sop.order.created", new
                    {
                        orderId = orderId.Value, // Use confirmed ID
                        orderRef,
                        externalId = request.Header.SourceExternalId,
                        tenantId,
                        correlationId,
                        createdUtc = DateTime.UtcNow
                    });
                    _log.LogInformation("Published sop.order.created event for OrderId {OrderId}", orderId.Value);
                }
                catch (Exception pubEx)
                {
                    _log.LogError(pubEx, "Failed to publish sop.order.created event for OrderId {OrderId}", orderId.Value);
                    // Don't fail the response for publish errors
                }
            }

            return new SopOrderCreateResult { Success = true, OrderId = orderId, OrderReference = orderRef, Message = "Created" };
        }

        /// <summary>
        /// [NEW ADDITION - STAGE 10]
        /// Maps the internal SopOrderCreate model to the SageSopOrder DTO for serialization.
        /// </summary>
        private static SageSopOrder MapCreateToSageDto(SopOrderCreate request)
        {
            var sageDto = new SageSopOrder
            {
                CustomerId = request.Header.CustomerId,
                CustomerReference = request.Header.CustomerReference,
                PromisedDeliveryDate = request.Header.PromisedDate, // Assuming DateTime? maps correctly via serializer
                // CurrencyCode is often derived from Customer in Sage, but allow override if provided
                // CurrencyId = MapCurrencyCodeToId(request.Header.CurrencyCode), // Requires lookup if needed
                SpareText1 = request.Header.SourceExternalId, // Map SourceExternalId to spare_text_1

                // Map Lines
                Lines = request.Lines?.Select(l => new SageSopOrderLine
                {
                    // Map required fields based on sop_order_line schema
                    // LineType needs mapping from string/enum if applicable (e.g., "EnumLineTypeStandard")
                    // Assuming standard lines for now:
                    LineType = "EnumLineTypeStandard", // Default or determine based on input?
                    Code = l.ProductCode, // Map ProductCode to 'code'
                    LineQuantity = l.Quantity,
                    SellingUnitPrice = l.UnitPrice,
                    Description = l.Description,
                    SpareText1 = l.SourceExternalLineId // Map SourceExternalLineId to spare_text_1

                    // Map other relevant fields if available in SopOrderCreateLine:
                    // WarehouseId = ...,
                    // TaxCodeId = ...,
                    // NominalReference = ...,
                    // RequestedDeliveryDate = ...,
                    // PromisedDeliveryDate = ...,
                    // Analysis Codes, etc.

                }).ToList()

                // Map Delivery Address if needed (SopOrderCreate doesn't have it, assumes customer default)
                // DeliveryAddress = new SageDeliveryAddress { ... }

                // Map other header fields if available in SopOrderCreateHeader:
                // DocumentDate = ...,
                // DocumentNo = ..., (If manual numbering)
                // SettlementDiscountDays = ...,
                // SettlementDiscountPercent = ...,
                // DocumentDiscountPercent = ...,
                // RequestedDeliveryDate = ..., (Header level)
                // Analysis Codes, etc.
            };

            return sageDto;
        }

        // --- Existing Helpers ---

        /// <summary>
        /// Converts Sage document_status literal to friendly short form.
        /// </summary>
        private static string? NormalizeDocumentStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var s = value.Trim();
            const string prefix = "EnumDocumentStatus";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(prefix.Length);
            }
            return s switch
            {
                "Complete" or "Completed" => "Completed",
                "Cancelled" or "Canceled" => "Cancelled",
                "OnHold" or "Held" => "OnHold",
                _ => s // "Live" and any other already-friendly values
            };
        }

        /// <summary>
        /// Helper to convert Sage DateTimeOffset? values into UTC DateTime?.
        /// Centralises null-safe conversion to avoid implicit cast errors.
        /// </summary>
        private static DateTime? ToNullableUtc(DateTimeOffset? value)
            => value?.UtcDateTime;

        /// <summary>
        /// Tries to get correlation ID from header or TraceIdentifier.
        /// </summary>
        private static string? TryGetCorrelationId(HttpContext http)
        {
            if (http.Request.Headers.TryGetValue("x-correlation-id", out var corr) && !string.IsNullOrWhiteSpace(corr))
                return corr.ToString();
            return http.TraceIdentifier;
        }

        /// <summary>
        /// Tries to get tenant ID from user claims.
        /// </summary>
        private static string? TryGetTenantId(ClaimsPrincipal user)
        {
            var tid = user.FindFirst("tid")?.Value
                  ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
            return string.IsNullOrWhiteSpace(tid) ? null : tid;
        }

        /// <summary>
        /// Hashes a string using SHA256 and returns Base64Url encoding.
        /// </summary>
        private static string? HashBase64Url(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            var b64 = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return b64;
        }

        /// <summary>
        /// [KEEP EXISTING - Copied from SalesInvoiceService] Returns a short, safe excerpt of text.
        /// </summary>
        private static string SafePreview(string? s, int max = 512)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public Task<SopOrderCreateResult> CreateSopOrderAsync(SopOrderPayload sopOrder, string customerUrn, RequestContext context, CancellationToken ct)
        {
            throw new NotImplementedException();
        }


        // --- Internal DTO for OData Deserialization ---

        /// <summary>
        /// [NEW internal DTO - STAGE 10]
        /// Represents the standard OData response structure for collections.
        /// Used for deserializing paged results from Sage API client GET calls.
        /// </summary>
        private class ODataResponse<T>
        {
            [JsonPropertyName("@odata.count")]
            public int? ODataCount { get; set; }

            [JsonPropertyName("value")]
            public List<T> Value { get; set; } = new List<T>();
        }
    }
}