// Purpose: Consume MDM_INVOICE_RESULTS, deserialize the envelope, update TransactionAttempt,
//          upsert ExternalIdLink (when externalRef present), write an AuditLog, and push DLQ on
//          permanent failures (then commit).
//
// Topic:           "MDM_INVOICE_RESULTS"
// DLQ Topic:       "MDM_INVOICE_RESULTS_DLQ"
// Consumer Group:  KafkaOptions.ConsumerGroupId (fallback: "sage200microservice_results")  // :contentReference[oaicite:6]{index=6}
//
// Feature Gating:  This HostedService will be registered only when Features:Kafka:Enabled == true
//                  in later DI/Program steps per the agreed sequence.
//
// Contracts Referenced (existing in your solution):
//  • KafkaOptions (Services/Messaging/KafkaOptions.cs)                         // ConsumerGroupId, BootstrapServers, etc.  :contentReference[oaicite:7]{index=7}
//
//  • TransactionAttempt (Data/Models/TransactionAttempt.cs)                    // ProcessingStatus (string), IdempotencyKeyHash, SageUrn, SageId, etc.  :contentReference[oaicite:8]{index=8}
//  • ApplicationContext (Data/ApplicationContext.cs)                           // Model configured in OnModelCreating; use db.Set<TransactionAttempt>()  :contentReference[oaicite:9]{index=9}
//  • ExternalIdLink (Data/Models/ExternalIdLink.cs + ExternalEntityType enum)  // Enum persisted as string via EF conversion
//
//  • AuditLog (Data/Models/AuditLog.cs)                                        // EventType/Category/Severity/Status + Action/Resource/Description      :contentReference[oaicite:11]{index=11}
//
//  • ResultMessageEnvelope (Services/Messaging/Consumers/Common/ResultMessageEnvelope.cs)  // delivered previously
//
//  • IEventPublisher (Services/Messaging/IEventPublisher.cs)                   // Already present in your codebase
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
    /// Hosted Kafka consumer for invoice result events.
    /// </summary>
    public sealed class InvoiceResultConsumer : BackgroundService
    {
        private const string TopicName = "MDM_INVOICE_RESULTS";
        private const string DlqTopicName = "MDM_INVOICE_RESULTS_DLQ";

        private readonly IServiceProvider _services;
        private readonly ILogger<InvoiceResultConsumer> _logger;
        private readonly IEventPublisher _eventPublisher;
        private readonly KafkaOptions _kafka;
        private readonly SageApiSettings _sage;
        private readonly IHostEnvironment _env;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// DI constructor.
        /// </summary>
        public InvoiceResultConsumer(
            IServiceProvider services,
            ILogger<InvoiceResultConsumer> logger,
            IEventPublisher eventPublisher,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<SageApiSettings> sageOptions,
            IHostEnvironment env)
        {
            _services = services;
            _logger = logger;
            _eventPublisher = eventPublisher;
            _kafka = kafkaOptions.Value;
            _sage = sageOptions.Value;
            _env = env;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var groupId = string.IsNullOrWhiteSpace(_kafka.ConsumerGroupId)
                ? "sage200microservice_results"
                : _kafka.ConsumerGroupId!; // correct property per KafkaOptions.cs  :contentReference[oaicite:12]{index=12}

            var config = new ConsumerConfig
            {
                BootstrapServers = _kafka.BootstrapServers,
                GroupId = groupId,
                EnableAutoCommit = _kafka.EnableAutoCommit, // default false per options  :contentReference[oaicite:13]{index=13}
                AutoOffsetReset = ParseOffsetReset(_kafka.AutoOffsetReset),
                // Additional SASL/SSL can be applied from KafkaOptions if configured
            };

            using var consumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
                .SetStatisticsHandler((_, s) => _logger.LogDebug("Kafka stats: {Stats}", s))
                .Build();

            consumer.Subscribe(TopicName);
            _logger.LogInformation("InvoiceResultConsumer subscribed to {Topic} (group {Group})", TopicName, groupId);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, string>? result = null;

                    try
                    {
                        result = consumer.Consume(stoppingToken);
                        if (result is null) continue;

                        await ProcessMessageAsync(result, stoppingToken).ConfigureAwait(false);

                        // Manual commit on success
                        if (!_kafka.EnableAutoCommit)
                            consumer.Commit(result);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (PermanentMessageException pmx)
                    {
                        _logger.LogWarning(pmx, "Permanent failure; sending to DLQ and committing.");

                        try
                        {
                            await PublishDlqAsync(result, pmx.Reason, stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception dlqEx)
                        {
                            _logger.LogError(dlqEx, "DLQ publish failed for topic {Topic}", TopicName);
                        }

                        // Commit regardless to avoid hot loop for permanent errors
                        if (result is not null && !_kafka.EnableAutoCommit)
                            consumer.Commit(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Transient processing error; will retry.");
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
        /// Core processing pipeline for one Kafka record.
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

            // Locate attempt by correlationId, else by idempotencyKey hash (SHA512 Base64, length 88).
            // (Matches IdempotencyRecords.KeyHash and TransactionAttempt.IdempotencyKeyHash conventions)  :contentReference[oaicite:14]{index=14}
            var attempt = await FindAttemptAsync(db, envelope, ct).ConfigureAwait(false);
            if (attempt is null)
                throw Permanent("No matching TransactionAttempt (correlationId/idempotencyKey).");

            // Map + update fields
            var now = DateTime.UtcNow;
            var completedUtc = envelope.ReceivedAtUtc?.UtcDateTime ?? now;
            attempt.ProcessingStatus = NormalizeStatus(envelope.Status);              // string status
            attempt.SageUrn = envelope.SageUrn ?? attempt.SageUrn;                   // string? (max 128)  :contentReference[oaicite:15]{index=15}
            attempt.SageId = envelope.SageId.HasValue ? (long?)envelope.SageId.Value : attempt.SageId; // long?
            attempt.ProcessingCompletedUtc = completedUtc;
            attempt.DurationMs = NormalizeDuration(envelope.DurationMs, attempt.ProcessingStartedUtc, completedUtc);
            attempt.ResultMessage = envelope.ToConciseResultMessage(TopicName);

            // Upsert ExternalIdLink if externalRef present
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

            // Write AuditLog (minimal safe population)
            db.AuditLogs.Add(new AuditLog
            {
                Timestamp = now,
                EventType = AuditEventType.DataModification,
                Category = AuditEventCategory.Business,
                Severity = MapSeverity(envelope.Status),   // Info|Error|Warning  :contentReference[oaicite:16]{index=16}
                Status = MapAuditStatus(envelope.Status), // Success|Failure|InProgress|Denied
                Resource = envelope.EntityType.ToString() ?? "SalesInvoice",
                Action = "ResultReceived",
                Description = attempt.ResultMessage ?? $"Result {envelope.Status} from {TopicName}",
                CorrelationId = attempt.CorrelationId,

                // Optional/non-essential fields—use empty strings to satisfy non-nullable CLR properties:
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

            _logger.LogInformation("Invoice result processed. Correlation={CorrelationId}, Status={Status}, URN={Urn}",
                attempt.CorrelationId, envelope.Status, attempt.SageUrn);
        }

        // ------------------------------- Helpers --------------------------------

        private static async Task<TransactionAttempt?> FindAttemptAsync(ApplicationContext db, ResultMessageEnvelope env, CancellationToken ct)
        {
            // 1) Try correlationId
            if (!string.IsNullOrWhiteSpace(env.CorrelationId))
            {
                var byCorr = await db.Set<TransactionAttempt>()
                    .FirstOrDefaultAsync(a => a.CorrelationId == env.CorrelationId, ct)
                    .ConfigureAwait(false);
                if (byCorr is not null) return byCorr;
            }

            // 2) Try idempotencyKey hash (SHA512 Base64 -> 88 chars)
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
            // ExternalEntityType is an enum; persisted as string via EF conversion.  :contentReference[oaicite:17]{index=17}
            var entityType = TryParseEntityType(entityTypeText, defaultValue: ExternalEntityType.SalesInvoice);

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
                // keep CreatedUtc as original creation time; no update timestamp on this table by design
            }
        }

        private static ExternalEntityType TryParseEntityType(string? text, ExternalEntityType defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(text) &&
                Enum.TryParse<ExternalEntityType>(text, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        private static AutoOffsetReset ParseOffsetReset(string? setting)
            => string.Equals(setting, nameof(AutoOffsetReset.Latest), StringComparison.OrdinalIgnoreCase)
                ? AutoOffsetReset.Latest
                : AutoOffsetReset.Earliest;

        private static string ComputeSha512Base64(string input)
        {
            using var sha = SHA512.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash); // length 88
        }

        private static PermanentMessageException Permanent(string reason) => new(reason);

        // ------------------------------- DLQ --------------------------------

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

        // --------------------------- Permanent Error ------------------------

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