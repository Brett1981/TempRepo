using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Models; // Added for AuditLog and enums
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Infrastructure;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Messaging.Contracts;
using Sage200Microservice.Services.Models; // For SageApiSettings
using Sage200Microservice.Services.Models.Sales;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sage200Microservice.Services.Processing; // ConsumerExecutionWrapper
using Sage200Microservice.Services.Messaging.Consumers.Common; // TryGetLastValue
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging.Consumers
{
    public sealed class SalesInvoiceCreateConsumer : BackgroundService
    {
        private readonly ILogger<SalesInvoiceCreateConsumer> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IKafkaConsumer _consumer;
        private readonly IEventPublisher _dlqPublisher;
        // No scoped services injected directly in constructor
        private readonly KafkaOptions _kafkaOptions;
        private readonly SageApiSettings _sageApiSettings;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IHostEnvironment _env;
        private readonly ConsumerExecutionWrapper _exec;

        // Wrapper for DLQ messages
        private record DlqEnvelope(string OriginalPayload, Dictionary<string, string> Headers);

        public SalesInvoiceCreateConsumer(
            ILogger<SalesInvoiceCreateConsumer> logger,
            IServiceScopeFactory scopeFactory,
            IKafkaConsumer consumer,
            IEventPublisher dlqPublisher,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<SageApiSettings> sageApiSettings,
            IHostEnvironment env,
            ConsumerExecutionWrapper exec)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _consumer = consumer;
            _dlqPublisher = dlqPublisher;
            _kafkaOptions = kafkaOptions.Value;
            _sageApiSettings = sageApiSettings.Value;
            _env = env;
            _exec = exec;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                // Add DefaultIgnoreCondition if needed for serialization, e.g.:
                // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var topic = _kafkaOptions.InvoiceCreateTopic ?? "MDM_Invoices";
            await _consumer.SubscribeAsync(new[] { topic }, stoppingToken);
            _logger.LogInformation("SalesInvoiceCreateConsumer subscribed to topic: {Topic}", topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<Ignore, string>? consumeResult = null;

                try
                {
                    _logger.LogDebug("Waiting for message on topic {Topic}...", topic);
                    consumeResult = await _consumer.ConsumeAsync(stoppingToken);

                    if (consumeResult is null || consumeResult.IsPartitionEOF)
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    var headers = consumeResult.Message.Headers;
                    var correlationId = GetHeaderValue(headers, "correlation_id") ?? Guid.NewGuid().ToString();
                    var payload = consumeResult.Message.Value;

                    // Standard wrapper: (10s → 30s → 2m) retry, attempt logging, DLQ on exhaustion
                    await _exec.ExecuteAsync(
                        correlationId: correlationId,
                        entityType: "Invoice",
                        originalTopic: consumeResult.Topic,
                        dlqTopic: $"{consumeResult.Topic}_DLQ",
                        partition: consumeResult.Partition.Value,
                        offset: consumeResult.Offset.Value,
                        originalPayload: payload,
                        handler: async ct =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var salesInvoicesService = scope.ServiceProvider.GetRequiredService<ISalesInvoicesService>();
                            var idempotencyRepo = scope.ServiceProvider.GetRequiredService<IIdempotencyRecordRepository>();
                            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

                            await ProcessMessageAsync(
                                salesInvoicesService,
                                idempotencyRepo,
                                auditLogService,
                                consumeResult,
                                correlationId,
                                ct);
                        },
                        isTransient: static ex =>
                            ex is TimeoutException
                            || ex is HttpRequestException
                            || ex.GetType().Name.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
                            || ex.GetType().Name.Contains("DbUpdateException", StringComparison.OrdinalIgnoreCase),
                        ct: stoppingToken);

                    // Commit after wrapper returns (either success OR DLQ publish completed in wrapper)
                    await _consumer.CommitAsync(consumeResult);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in consumer loop; backing off 30s.");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }

            _consumer.Close();
            _logger.LogInformation("SalesInvoiceCreateConsumer stopped for topic: {Topic}", topic);
        }


        private async Task ProcessMessageAsync(
            ISalesInvoicesService salesInvoicesService,
            IIdempotencyRecordRepository idempotencyRepo,
            IAuditLogService auditLogService,
            ConsumeResult<Ignore, string> consumeResult,
            string correlationId, // Pass guaranteed non-null correlationId
            CancellationToken stoppingToken)
        {
            KafkaInvoiceCreateMessage? kafkaDto = null;
            Headers headers = consumeResult.Message.Headers;
            string originalValue = consumeResult.Message.Value;
            string? idempotencyKey = GetHeaderValue(headers, "idempotency-key");

            // Scope logger with correlation ID
            using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                try
                {
                    // 1. Deserialize
                    kafkaDto = JsonSerializer.Deserialize<KafkaInvoiceCreateMessage>(originalValue, _jsonOptions);
                    if (kafkaDto is null) throw new JsonException("Deserialized message is null.");
                    _logger.LogDebug("Message deserialized successfully.");

                    // --- STAGE 3 Idempotency Check ---
                    if (string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        _logger.LogError("Missing required 'idempotency-key' header. Sending message to DLQ. Offset: {Offset}", consumeResult.Offset);
                        await HandleFailureAsync(auditLogService, // Pass audit service
                            "MissingIdempotencyKey", "Required 'idempotency-key' header was not found or empty.",
                            consumeResult, originalValue, headers, correlationId, stoppingToken);
                        return;
                    }

                    var keyHash = HashKeySha512Base64(idempotencyKey);
                    _logger.LogDebug("Checking idempotency for KeyHash: {KeyHash}", keyHash);
                    var existingRecord = await idempotencyRepo.GetByKeyHashAsync(keyHash, stoppingToken);

                    if (existingRecord != null)
                    {
                        _logger.LogInformation("Duplicate message detected based on idempotency key (Hash: {KeyHash}). ResultSageUrn: {Urn}. Skipping processing and committing offset {Offset}.",
                            keyHash, existingRecord.ResultSageUrn ?? "N/A", consumeResult.Offset);

                        // --- Audit Duplicate Skip ---
                        await TryAuditAsync(auditLogService, AuditEventType.DataModification, AuditEventCategory.Business, AuditEventSeverity.Info, AuditEventStatus.Success, // Treat skip as success for audit status
                            "SalesInvoice", "CreateViaKafka", $"Skipped duplicate message based on idempotency key.",
                            consumeResult.Offset.Value.ToString(), "KafkaOffset", correlationId,
                            new { reason = "DuplicateIdempotencyKey", keyHash }, // Use hash, not raw key
                            stoppingToken);
                        // --- End Audit ---

                        await _consumer.CommitAsync(consumeResult);
                        return; // Skip processing
                    }
                    _logger.LogDebug("Idempotency key is new. Proceeding with processing.");
                    // --- End STAGE 3 Idempotency Check ---

                    // 2. Build RequestContext (with fallback from SageApiSettings)
                    string? site = GetHeaderValue(headers, "x-site") ?? _sageApiSettings.SiteId;
                    string? company = GetHeaderValue(headers, "x-company") ?? _sageApiSettings.CompanyId;

                    if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(company))
                    {
                        throw new InvalidOperationException($"Missing required context: SiteId='{site ?? "N/A"}' (Source: {GetSource(headers, "x-site", _sageApiSettings.SiteId)}), CompanyId='{company ?? "N/A"}' (Source: {GetSource(headers, "x-company", _sageApiSettings.CompanyId)}). Cannot process message.");
                    }

                    if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null; // Should not happen due to check above, but defensive
                    // === NEW: resolve API key (header → dev fallback) ===
                    string? apiKey = GetHeaderValue(headers, _sageApiSettings.ApiKeyHeaderName);
                    if (string.IsNullOrWhiteSpace(apiKey) && _env.IsDevelopment() && _sageApiSettings.AllowDevelopmentFallbackApiKey)
                    {
                        apiKey = _sageApiSettings.DevelopmentDefaultApiKey;
                    }

                    // === NEW: push ambient context for downstream HttpClient calls ===
                    using var __ambient = SageCallContext.Push(site, company, apiKey);
                    var requestContext = new RequestContext(site, company, idempotencyKey, correlationId);
                    _logger.LogDebug("RequestContext created: Site={SiteId} (Source: {SiteSource}), Company={CompanyId} (Source: {CompanySource}), IdemKeyPresent={IdemKeyPresent}",
                        requestContext.SiteId, GetSource(headers, "x-site", _sageApiSettings.SiteId),
                        requestContext.CompanyId, GetSource(headers, "x-company", _sageApiSettings.CompanyId),
                        idempotencyKey != null ? "Present" : "N/A");

                    // 3. Map Kafka DTO to Service DTO
                    var serviceDto = MapToServiceDto(kafkaDto);
                    _logger.LogDebug("Mapped Kafka DTO to Service DTO.");
                    // === UAT FAULT INJECTION (guarded by config) ===
                    // If enabled and a test header is present, simulate the requested failure mode.
                    // X-Fault values supported: SAGE_503_ONCE | DB_TIMEOUT | INVALID_PAYLOAD
                    if (_sageApiSettings.EnableFaultInjection)
                    {
                        var faultKey = GetHeaderValue(headers, "X-Fault");
                        if (!string.IsNullOrWhiteSpace(faultKey))
                        {
                            switch (faultKey.Trim().ToUpperInvariant())
                            {
                                case "SAGE_503_ONCE":
                                    // Simulate a transient downstream failure (HTTP 503) to exercise retry backoff.
                                    throw new HttpRequestException(
                                        "Simulated Sage 503",
                                        inner: null,
                                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable);

                                case "DB_TIMEOUT":
                                    // Simulate a transient database timeout to exercise retry backoff.
                                    throw new TimeoutException("Simulated DB timeout");

                                case "INVALID_PAYLOAD":
                                    // Simulate a permanent error; wrapper will route directly to DLQ (no retries).
                                    throw new InvalidOperationException("Simulated permanent validation failure");
                            }
                        }
                    }
                    // === END FAULT INJECTION ===
                    // 4. Call Service
                    _logger.LogInformation("Calling SalesInvoicesService.CreateAsync with IdempotencyKey: {IdemKeyStatus}...", idempotencyKey != null ? "Present" : "N/A");
                    SalesCreateResult result = await salesInvoicesService.CreateAsync(serviceDto, requestContext, stoppingToken);

                    // 5. Handle Result
                    if (result.Success && !string.IsNullOrWhiteSpace(result.Urn)) // Check URN is present on success
                    {
                        _logger.LogInformation("SalesInvoicesService.CreateAsync succeeded. URN: {Urn}. Committing offset {Offset}.", result.Urn, consumeResult.Offset);

                        // Commit Kafka offset FIRST (if this fails, we don't audit success)
                        await _consumer.CommitAsync(consumeResult);

                        // --- Audit Success (AFTER commit) ---
                        await TryAuditAsync(auditLogService, AuditEventType.DataModification, AuditEventCategory.Business, AuditEventSeverity.Info, AuditEventStatus.Success,
                            "SalesInvoice", "CreateViaKafka", "Sales invoice created successfully via Kafka.",
                            result.Urn, "SageURN", correlationId,
                            new { urn = result.Urn, customerId = serviceDto.CustomerId }, // Minimal success details
                            stoppingToken);
                        // --- End Audit ---
                    }
                    else // Includes service failure OR success flag but missing URN
                    {
                        string failureReason = result.Success ? "MissingUrnOnSuccess" : "ServiceReportedFailure";
                        string failureMessage = result.Success ? "Service reported success but returned no URN." : (result.Message ?? "Service failed");

                        _logger.LogWarning("SalesInvoicesService.CreateAsync failed or returned invalid success: {Message}. Status: {Status}, Body: {Body}. Sending to DLQ. Offset: {Offset}",
                            failureMessage, result.UpstreamStatusCode, result.UpstreamBody, consumeResult.Offset);

                        // Failure audit happens inside HandleFailureAsync before commit
                        await HandleFailureAsync(auditLogService, // Pass audit service
                             failureReason, failureMessage,
                             consumeResult, originalValue, headers, correlationId, stoppingToken, result.UpstreamStatusCode);
                    }
                }
                // --- Exception Handling: Catch specific exceptions first ---
                catch (JsonException jsonEx) // Deserialization failed
                {
                    _logger.LogError(jsonEx, "JSON Deserialization failed for message at {TopicPartitionOffset}.", consumeResult.TopicPartitionOffset);
                    await HandleFailureAsync(auditLogService, "DeserializationFailed", jsonEx.Message, consumeResult, originalValue, headers, correlationId, stoppingToken);
                }
                catch (FormatException formatEx) // Date parsing failed
                {
                    _logger.LogError(formatEx, "Date parsing failed during mapping for message at {TopicPartitionOffset}.", consumeResult.TopicPartitionOffset);
                    await HandleFailureAsync(auditLogService, "MappingFailed", formatEx.Message, consumeResult, originalValue, headers, correlationId, stoppingToken);
                }
                catch (InvalidOperationException invalidOpEx) // Context validation failed (missing headers/fallback)
                {
                    _logger.LogError(invalidOpEx, "Context validation failed for message at {TopicPartitionOffset}.", consumeResult.TopicPartitionOffset);
                    await HandleFailureAsync(auditLogService, "ContextValidationFailed", invalidOpEx.Message, consumeResult, originalValue, headers, correlationId, stoppingToken);
                }
                // --- Catch-all for unexpected errors during processing ---
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error processing message at {TopicPartitionOffset}.", consumeResult.TopicPartitionOffset);
                    await HandleFailureAsync(auditLogService, "ProcessingException", ex.Message, consumeResult, originalValue, headers, correlationId, stoppingToken);
                }
            } // End logging scope
        }

        private async Task HandleFailureAsync(
            IAuditLogService auditLogService, // Added audit service
            string reason, string errorMessage, ConsumeResult<Ignore, string> consumeResult, string originalValue,
            Headers originalHeaders, string correlationId, CancellationToken stoppingToken, int? upstreamStatusCode = null)
        {
            var dlqTopic = $"{_kafkaOptions.InvoiceCreateTopic ?? "MDM_Invoices"}.dlq";
            _logger.LogInformation("Publishing failed message to DLQ topic: {DlqTopic} for reason: {Reason}", dlqTopic, reason);

            var dlqHeadersDict = new Dictionary<string, string>();
            // Add DLQ context
            dlqHeadersDict["dlq_reason"] = reason;
            dlqHeadersDict["dlq_error_message"] = Truncate(errorMessage, 1024) ?? "N/A"; // Cap error message length
            dlqHeadersDict["dlq_original_topic"] = consumeResult.Topic;
            dlqHeadersDict["dlq_original_partition"] = consumeResult.Partition.Value.ToString();
            dlqHeadersDict["dlq_original_offset"] = consumeResult.Offset.Value.ToString();
            dlqHeadersDict["dlq_timestamp"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (upstreamStatusCode.HasValue)
            {
                dlqHeadersDict["dlq_upstream_status"] = upstreamStatusCode.Value.ToString();
            }
            // Ensure correlationId is included if not already in original headers
            if (!dlqHeadersDict.ContainsKey("correlation_id"))
            {
                dlqHeadersDict["correlation_id"] = correlationId;
            }


            // Copy and decode original headers
            foreach (var header in originalHeaders)
            {
                // Avoid duplicating DLQ headers if they happen to be in original
                if (!dlqHeadersDict.ContainsKey(header.Key) && !header.Key.StartsWith("dlq_"))
                {
                    try
                    {
                        dlqHeadersDict[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decode header '{HeaderKey}' to UTF8 for DLQ message. Using placeholder.", header.Key);
                        dlqHeadersDict[header.Key] = "[DECODING_ERROR]";
                    }
                }
            }

            var dlqEnvelope = new DlqEnvelope(originalValue, dlqHeadersDict);

            try
            {
                // --- Audit Failure (BEFORE DLQ publish attempt & commit) ---
                var severity = reason switch
                {
                    "MissingIdempotencyKey" => AuditEventSeverity.Warning,
                    "ContextValidationFailed" => AuditEventSeverity.Warning,
                    "DeserializationFailed" => AuditEventSeverity.Warning,
                    "MappingFailed" => AuditEventSeverity.Warning,
                    _ => AuditEventSeverity.Error // ServiceReportedFailure, ProcessingException, UnhandledException etc.
                };
                await TryAuditAsync(auditLogService, AuditEventType.DataModification, AuditEventCategory.Business, severity, AuditEventStatus.Failure,
                    "SalesInvoice", "CreateViaKafka", $"Sales invoice create via Kafka failed: {reason}.",
                    consumeResult.Offset.Value.ToString(), "KafkaOffset", correlationId,
                    // Minimal, safe details for failure audit
                    new { errorReason = reason, errorMessage = Truncate(errorMessage, 256), upstreamStatus = upstreamStatusCode },
                    stoppingToken);
                // --- End Audit ---

                // Now attempt to publish to DLQ
                await _dlqPublisher.PublishAsync(dlqTopic, dlqEnvelope, stoppingToken);
                _logger.LogInformation("Message published to DLQ: {DlqTopic} for original offset {Offset}", dlqTopic, consumeResult.Offset);

                // Commit the original message offset *only after* successfully auditing and publishing to DLQ
                await _consumer.CommitAsync(consumeResult);
                _logger.LogInformation("Committed original message offset {Offset} after DLQ.", consumeResult.Offset);
            }
            catch (Exception pubEx) // Catch failure during audit or DLQ publish
            {
                _logger.LogError(pubEx, "CRITICAL: Failed during audit or DLQ publish for {Offset}. Reason: {Reason}. Original offset will NOT be committed.", consumeResult.Offset, reason);
                // DO NOT COMMIT OFFSET
            }
        }

        // --- Mapping, Parsing, Helper Methods ---
        // (MapToServiceDto, ParseDate, GetHeaderValue, GetSource, HashKeySha512Base64 remain the same as previous correct version)
        // ... (Include the full methods here from the previous correct response)

        private SalesInvoiceCreate MapToServiceDto(KafkaInvoiceCreateMessage kafkaDto)
        {
            DateTimeOffset? transactionDate = ParseDate(kafkaDto.TransactionDate, nameof(kafkaDto.TransactionDate));
            DateTimeOffset? dueDate = ParseDate(kafkaDto.DueDate, nameof(kafkaDto.DueDate));

            return new SalesInvoiceCreate
            {
                CustomerId = kafkaDto.CustomerId,
                TransactionDate = transactionDate,
                DueDate = dueDate,
                ExchangeRate = kafkaDto.ExchangeRate,
                SettledImmediately = kafkaDto.SettledImmediately,
                DocumentGoodsValue = kafkaDto.DocumentGoodsValue,
                DocumentTaxValue = kafkaDto.DocumentTaxValue,
                DocumentDiscountValue = kafkaDto.DocumentDiscountValue,
                DocumentTaxDiscountValue = kafkaDto.DocumentTaxDiscountValue,
                DiscountPercent = kafkaDto.DiscountPercent,
                DiscountDays = kafkaDto.DiscountDays,
                TriangularTransaction = kafkaDto.TriangularTransaction,
                Reference = kafkaDto.Reference,
                SecondReference = kafkaDto.SecondReference,
                TaxAnalysisItems = kafkaDto.TaxAnalysisItems?.Select(k => new SalesInvoiceCreate.TaxAnalysisItem { Id = k.Id, GoodsAmount = k.GoodsAmount, DiscountAmount = k.DiscountAmount, TaxAmount = k.TaxAmount, TaxDiscountAmount = k.TaxDiscountAmount }).ToList(),
                NominalAnalysisItems = kafkaDto.NominalAnalysisItems?.Select(k => new SalesInvoiceCreate.NominalAnalysisItem { Code = k.Code, CostCentre = k.CostCentre, Department = k.Department, Narrative = k.Narrative, Value = k.Value, TransactionAnalysisCode = k.TransactionAnalysisCode }).ToList(),
                ExternalRefs = kafkaDto.ExternalRefs?.Select(k => new SalesInvoiceCreate.ExternalRefItem { AppId = k.AppId, ExternalRef = k.ExternalRef }).ToList()
            };
        }
        private DateTimeOffset? ParseDate(string? dateString, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(dateString)) return null;
            if (DateTimeOffset.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate)) { return parsedDate; }
            throw new FormatException($"Invalid {fieldName} format: '{dateString}'. Expected ISO 8601 UTC format (e.g., YYYY-MM-DDTHH:mm:ssZ).");
        }
        private string? GetHeaderValue(Headers headers, string key)
        {
            var header = headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
            if (header != null) { try { byte[] bytes = header.GetValueBytes(); if (bytes != null) return Encoding.UTF8.GetString(bytes); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to decode header '{HeaderKey}' to UTF8. Treating as null.", key); } }
            return null;
        }
        private string GetSource(Headers headers, string key, string? fallbackValue)
        {
            if (GetHeaderValue(headers, key) != null) return "Header";
            if (!string.IsNullOrWhiteSpace(fallbackValue)) return "Fallback";
            return "Missing";
        }
        private static string HashKeySha512Base64(string key)
        {
            using var sha = SHA512.Create();
            var bytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
        // Helper to safely call audit service and log failures
        private async Task TryAuditAsync(
            IAuditLogService auditLogService,
            AuditEventType eventType, AuditEventCategory category, AuditEventSeverity severity, AuditEventStatus status,
            string resource, string action, string description,
            string? referenceId, string? referenceName, string correlationId,
            object? details, CancellationToken cancellationToken)
        {
            try
            {
                string? detailsJson = null;
                if (details != null)
                {
                    detailsJson = Truncate(JsonSerializer.Serialize(details, _jsonOptions), 2048); // Cap at ~2KB
                }

                // Assuming LogDataModificationEventAsync can handle different event types/categories/severities
                // Or you might need separate IAuditLogService methods (e.g., LogAuditEventAsync)
                // For now, using LogDataModificationEventAsync as it takes most fields
                await auditLogService.LogDataModificationEventAsync(
                    userId: null, // Kafka consumers typically don't have a user context
                    clientId: _kafkaOptions.ClientId, // Use configured ClientId
                    ipAddress: null, // No IP address context for Kafka consumer
                    resource: resource,
                    referenceId: referenceId,
                    referenceName: referenceName,
                    action: action,
                    status: status,
                    description: description,
                    previousState: null, // Not typically tracked for Kafka events unless needed
                    newState: null,      // "
                    details: detailsJson ?? "{}", // Pass serialized & truncated JSON
                    correlationId: correlationId); // Pass guaranteed non-null correlationId
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log entry for {Resource}/{Action}. CorrelationId: {CorrelationId}", resource, action, correlationId);
                // Do not rethrow, audit failure should not stop processing
            }
        }
        // Simple string truncation helper
        private static string? Truncate(string? value, int maxLength) =>
            value?.Length > maxLength ? value.Substring(0, maxLength) + "..." : value;

    } // End class SalesInvoiceCreateConsumer
} // End namespace