// Purpose:
//   Consume MDM_CUSTOMER_RESULTS, deserialize the ResultMessageEnvelope, update TransactionAttempt,
//   upsert ExternalIdLink (when externalRef present), write one AuditLog entry, and on permanent
//   failure publish to MDM_CUSTOMER_RESULTS_DLQ then commit.
//
// Notes:
//   • No DI/Program changes here (feature gating will be added later per sequence).
//   • Mirrors InvoiceResultConsumer, but with resource "Customer" and entity-type targeting Customer.
//   • Uses KafkaOptions.ConsumerGroupId, AutoOffsetReset, EnableAutoCommit.
// =====================================================================================================
namespace Sage200Microservice.Services.Messaging.Consumers.Results
{
    using Confluent.Kafka;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Sage200Microservice.Data;
    using Sage200Microservice.Data.Models;
    using Sage200Microservice.Services.Infrastructure;
    using Sage200Microservice.Services.Messaging;
    using Sage200Microservice.Services.Processing; // ConsumerExecutionWrapper
    using Sage200Microservice.Services.Messaging.Consumers.Common;
    using Sage200Microservice.Services.Models;
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;


    /// <summary>
    /// Hosted Kafka consumer for customer result events (MDM_CUSTOMER_RESULTS).
    /// </summary>
    public sealed class CustomerResultConsumer : BackgroundService
    {
        private const string TopicName = "MDM_CUSTOMER_RESULTS";
        private const string DlqTopicName = "MDM_CUSTOMER_RESULTS_DLQ";

        private readonly IServiceProvider _services;
        private readonly ILogger<CustomerResultConsumer> _logger;
        private readonly IEventPublisher _eventPublisher;
        private readonly KafkaOptions _kafka;
        private readonly SageApiSettings _sage;
        private readonly IHostEnvironment _env;
        private readonly ConsumerExecutionWrapper _exec;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// DI constructor.
        /// </summary>
        public CustomerResultConsumer(
            IServiceProvider services,
            ILogger<CustomerResultConsumer> logger,
            IEventPublisher eventPublisher,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<SageApiSettings> sageOptions,
            IHostEnvironment env,
            ConsumerExecutionWrapper exec)
        {
            _services = services;
            _logger = logger;
            _eventPublisher = eventPublisher;
            _kafka = kafkaOptions.Value;
            _sage = sageOptions.Value;
            _env = env;
            _exec = exec;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var groupId = string.IsNullOrWhiteSpace(_kafka.ConsumerGroupId)
                ? "sage200microservice_results"
                : _kafka.ConsumerGroupId!;

            var config = new ConsumerConfig
            {
                BootstrapServers = _kafka.BootstrapServers,
                GroupId = groupId,
                EnableAutoCommit = _kafka.EnableAutoCommit,
                AutoOffsetReset = ParseOffsetReset(_kafka.AutoOffsetReset),
            };

            using var consumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
                .SetStatisticsHandler((_, s) => _logger.LogDebug("Kafka stats: {Stats}", s))
                .Build();

            consumer.Subscribe(TopicName);
            _logger.LogInformation("CustomerResultConsumer subscribed to {Topic} (group {Group})", TopicName, groupId);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, string>? result = null;

                    try
                    {
                        result = consumer.Consume(stoppingToken);
                        if (result is null) continue;

                        var key = result.Message?.Key ?? Guid.NewGuid().ToString();
                        var payload = result.Message?.Value ?? string.Empty;

                        await _exec.ExecuteAsync(
                            correlationId: key,
                            entityType: "Customer",
                            originalTopic: TopicName,
                            dlqTopic: DlqTopicName,
                            partition: result.Partition.Value,
                            offset: result.Offset.Value,
                            originalPayload: payload,
                            handler: ct => ProcessMessageAsync(result, ct),
                            isTransient: static ex =>
                                ex is TimeoutException
                                || ex is HttpRequestException
                                || ex.GetType().Name.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
                                || ex.GetType().Name.Contains("DbUpdateException", StringComparison.OrdinalIgnoreCase),
                            ct: stoppingToken);

                        if (!_kafka.EnableAutoCommit)
                            consumer.Commit(result);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Transient processing error in result consumer; will retry in 2s.");
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                consumer.Close();
            }
        }


        /// <summary>
        /// Processes a single Kafka message for customer results.
        /// </summary>
        private async Task ProcessMessageAsync(ConsumeResult<string, string> record, CancellationToken ct)
        {
            // -------- Ambient Sage context (headers → ambient → defaults) --------
            var msgHeaders = record.Message.Headers;
            var site = msgHeaders.TryGetLastValue(_sage.SiteHeaderName) ?? _sage.SiteId;
            var company = msgHeaders.TryGetLastValue(_sage.CompanyHeaderName) ?? _sage.CompanyId;

            // API key: prefer header; in Development we allow fallback to configured dev key
            var apiKey = msgHeaders.TryGetLastValue(_sage.ApiKeyHeaderName);
            if (string.IsNullOrWhiteSpace(apiKey) && _env.IsDevelopment() && _sage.AllowDevelopmentFallbackApiKey)
            {
                apiKey = _sage.DevelopmentDefaultApiKey;
            }

            // Push ambient context so the SageRoutingHeaderHandler can inject headers on any downstream Sage calls
            using var __ambient = SageCallContext.Push(site, company, apiKey);
            // --------------------------------------------------------------------

            var payload = record.Message?.Value;
            if (string.IsNullOrWhiteSpace(payload))
                throw Permanent("Empty payload.");

            // ===========================
            // UAT FAULT INJECTION (guarded by config)
            // Triggered by header: X-Fault
            //   - SAGE_503_ONCE  => transient (simulate HTTP 503) → retried by wrapper
            //   - DB_TIMEOUT     => transient (TimeoutException)   → retried by wrapper
            //   - INVALID_PAYLOAD=> permanent (no retries)         → DLQ immediately
            // ===========================
            if (_sage.EnableFaultInjection)
            {
                var faultKey = msgHeaders.TryGetLastValue("X-Fault");
                if (!string.IsNullOrWhiteSpace(faultKey))
                {
                    switch (faultKey.Trim().ToUpperInvariant())
                    {
                        case "SAGE_503_ONCE":
                            // Transient downstream error (HTTP 503)
                            throw new HttpRequestException(
                                "Simulated Sage 503",
                                inner: null,
                                statusCode: System.Net.HttpStatusCode.ServiceUnavailable);

                        case "DB_TIMEOUT":
                            // Transient infrastructure error (DB timeout)
                            throw new TimeoutException("Simulated DB timeout");

                        case "INVALID_PAYLOAD":
                            // Permanent error: wrapper will route to DLQ with no retries
                            throw Permanent("Simulated permanent validation failure");
                    }
                }
            }
            // ===== END FAULT INJECTION =====

            ResultMessageEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<ResultMessageEnvelope>(payload, _jsonOptions);
            }
            catch (Exception jex)
            {
                throw Permanent($"Deserialization error: {jex.Message}");
            }
            if (envelope is null)
                throw Permanent("Envelope deserialized to null.");

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            // Lookup TransactionAttempt: correlationId first, then IdempotencyKey hash (SHA-512 Base64, 88 chars)
            var attempt = await FindAttemptAsync(db, envelope, ct).ConfigureAwait(false);
            if (attempt is null)
                throw Permanent("No matching TransactionAttempt (correlationId/idempotencyKey).");

            var now = DateTime.UtcNow;
            var completedUtc = envelope.ReceivedAtUtc?.UtcDateTime ?? now;

            // Update attempt
            attempt.ProcessingStatus = NormalizeStatus(envelope.Status);
            attempt.SageUrn = envelope.SageUrn ?? attempt.SageUrn;
            attempt.SageId = envelope.SageId.HasValue ? (long?)envelope.SageId.Value : attempt.SageId;
            attempt.ProcessingCompletedUtc = completedUtc;
            attempt.DurationMs = NormalizeDuration(envelope.DurationMs, attempt.ProcessingStartedUtc, completedUtc);
            attempt.ResultMessage = envelope.ToConciseResultMessage(TopicName);

            // Upsert ExternalIdLink if externalRef present (AppId from envelope.apiKeyId fallback to attempt.ApiKeyId)
            if (!string.IsNullOrWhiteSpace(envelope.ExternalRef))
            {
                var appId = envelope.ApiKeyId ?? attempt.ApiKeyId ?? 0;
                if (appId > 0)
                {
                    await UpsertExternalIdLinkAsync(
                        db,
                        appId,
                        envelope.ExternalRef!,
                        envelope.EntityType.ToString(),
                        attempt.SageUrn,
                        attempt.SageId,
                        now,
                        ct).ConfigureAwait(false);
                }
            }

            // Write AuditLog
            db.AuditLogs.Add(new AuditLog
            {
                Timestamp = now,
                EventType = AuditEventType.DataModification,
                Category = AuditEventCategory.Business,
                Severity = MapSeverity(envelope.Status),
                Status = MapAuditStatus(envelope.Status),
                Resource = "Customer",
                Action = "ResultReceived",
                Description = attempt.ResultMessage ?? $"Result {envelope.Status} from {TopicName}",
                CorrelationId = attempt.CorrelationId,

                // Optional/non-essential fields:
                IpAddress = string.Empty,
                ClientId = attempt.ApiKeyId?.ToString(),
                UserId = null,
                Details = string.Empty,
                HttpMethod = string.Empty,
                UrlPath = string.Empty,
                UserAgent = string.Empty,
                ReferenceId = null,
                ReferenceName = null,
                PreviousState = null,
                NewState = null,
                DurationMs = attempt.DurationMs,
                RetentionDays = 0,
                ExpiresAt = null
            });

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Customer result processed. Correlation={CorrelationId}, Status={Status}, URN={Urn}",
                attempt.CorrelationId, envelope.Status, attempt.SageUrn);
        }

        // ------------------------------ Helpers ------------------------------

        private static async Task<TransactionAttempt?> FindAttemptAsync(ApplicationContext db, ResultMessageEnvelope env, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(env.CorrelationId))
            {
                var byCorr = await db.Set<TransactionAttempt>()
                    .FirstOrDefaultAsync(a => a.CorrelationId == env.CorrelationId, ct)
                    .ConfigureAwait(false);
                if (byCorr is not null) return byCorr;
            }

            if (!string.IsNullOrWhiteSpace(env.IdempotencyKey))
            {
                var keyHash = ComputeSha512Base64(env.IdempotencyKey!);
                var byKey = await db.Set<TransactionAttempt>()
                    .FirstOrDefaultAsync(a => a.IdempotencyKeyHash == keyHash, ct)
                    .ConfigureAwait(false);
                if (byKey is not null) return byKey;
            }

            return null;
        }

        private static string NormalizeStatus(string? status)
            => string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) ? "SageSuccess" : "SageFailure";

        private static int? NormalizeDuration(long? reportedMs, DateTime? startedUtc, DateTime completedUtc)
        {
            if (reportedMs.HasValue)
            {
                return reportedMs.Value > int.MaxValue ? int.MaxValue : (int)reportedMs.Value;
            }

            if (startedUtc.HasValue)
            {
                var ms = (completedUtc - startedUtc.Value).TotalMilliseconds;
                if (ms < 0) ms = 0;
                return ms > int.MaxValue ? int.MaxValue : (int)ms;
            }

            return null;
        }

        private static AuditEventSeverity MapSeverity(string? status)
        {
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) return AuditEventSeverity.Info;
            if (string.Equals(status, "Failure", StringComparison.OrdinalIgnoreCase)) return AuditEventSeverity.Error;
            return AuditEventSeverity.Warning;
        }

        private static AuditEventStatus MapAuditStatus(string? status)
        {
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) return AuditEventStatus.Success;
            if (string.Equals(status, "Failure", StringComparison.OrdinalIgnoreCase)) return AuditEventStatus.Failure;
            return AuditEventStatus.InProgress;
        }

        private static async Task UpsertExternalIdLinkAsync(
            ApplicationContext db,
            int appId,
            string externalRef,
            string? entityTypeText,
            string? sageUrn,
            long? sageId,
            DateTime nowUtc,
            CancellationToken ct)
        {
            var entityType = TryParseEntityType(entityTypeText, preferredText: "Customer");

            var existing = await db.ExternalIdLinks
                .FirstOrDefaultAsync(x => x.AppId == appId && x.EntityType == entityType && x.ExternalRef == externalRef, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                db.ExternalIdLinks.Add(new ExternalIdLink
                {
                    AppId = appId,
                    EntityType = entityType,
                    ExternalRef = externalRef,
                    SageUrn = sageUrn,
                    SageId = sageId,
                    CreatedUtc = nowUtc
                });
            }
            else
            {
                existing.SageUrn = sageUrn ?? existing.SageUrn;
                existing.SageId = sageId ?? existing.SageId;
            }
        }

        /// <summary>
        /// Attempts to parse the inbound entityType text to ExternalEntityType. If parsing fails, prefer "Customer"
        /// when available; else fall back to the enum's default (first value).
        /// </summary>
        private static ExternalEntityType TryParseEntityType(string? text, string preferredText)
        {
            if (!string.IsNullOrWhiteSpace(text) &&
                Enum.TryParse<ExternalEntityType>(text, ignoreCase: true, out var parsedFromPayload))
            {
                return parsedFromPayload;
            }

            if (Enum.TryParse<ExternalEntityType>(preferredText, ignoreCase: true, out var preferred))
            {
                return preferred;
            }

            // Final fallback: first defined enum value (safe default).
            var values = (ExternalEntityType[])Enum.GetValues(typeof(ExternalEntityType));
            return values.Length > 0 ? values[0] : default;
        }

        private static AutoOffsetReset ParseOffsetReset(string? setting)
            => string.Equals(setting, nameof(AutoOffsetReset.Latest), StringComparison.OrdinalIgnoreCase)
                ? AutoOffsetReset.Latest
                : AutoOffsetReset.Earliest;

        private static string ComputeSha512Base64(string input)
        {
            using var sha = SHA512.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash); // 88 chars
        }

        private static PermanentMessageException Permanent(string reason) => new(reason);

        // ------------------------------ DLQ ------------------------------

        private async Task PublishDlqAsync(ConsumeResult<string, string>? record, string reason, CancellationToken ct)
        {
            var headers = record?.Message?.Headers != null
                    ? record.Message.Headers.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
                    : new Dictionary<string, string>();
            var key = record?.Message?.Key;

            var dlq = new
            {
                correlationId = key,
                reason,
                originalPayload = record?.Message?.Value ?? string.Empty,
                headers,
                occurredUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dlq, _jsonOptions);
            await _eventPublisher.PublishAsync(DlqTopicName, json, ct).ConfigureAwait(false);
        }

        // ------------------------ Permanent Error ------------------------

        private sealed class PermanentMessageException : Exception
        {
            public string? Reason { get; }
            public PermanentMessageException(string reason) : base(reason) => Reason = reason;

            public PermanentMessageException() : base()
            {
                Reason = null;
            }

            public PermanentMessageException(string? message, Exception? innerException) : base(message, innerException)
            {
                Reason = message;
            }
        }
    }
}