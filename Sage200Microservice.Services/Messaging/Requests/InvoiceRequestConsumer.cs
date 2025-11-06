using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models; // for RequestContext (SiteId, CompanyId, IdempotencyKey, CorrelationId)

namespace Sage200Microservice.Services.Messaging.Consumers.Requests
{
    /// <summary>
    /// Consumes invoice requests from Kafka (MDM_INVOICES).
    /// Validates headers (api_key/site/company/idempotency), persists a TransactionAttempt(Received),
    /// deserializes MdmInvoiceMessage, builds RequestContext, and hands off to the request orchestrator.
    /// </summary>
    public sealed class InvoiceRequestConsumer : BackgroundService
    {
        private const string TopicName = "MDM_INVOICES";
        private const string DlqTopicName = "MDM_INVOICES_DLQ";

        private readonly IServiceProvider _services;
        private readonly ILogger<InvoiceRequestConsumer> _logger;
        private readonly IEventPublisher _publisher;               // for DLQ (and any future emits if needed)
        private readonly KafkaOptions _kafka;
        private readonly JsonSerializerOptions _json;

        public InvoiceRequestConsumer(
            IServiceProvider services,
            ILogger<InvoiceRequestConsumer> logger,
            IEventPublisher publisher,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<JsonSerializerOptions> jsonOptions)
        {
            _services = services;
            _logger = logger;
            _publisher = publisher;
            _kafka = kafkaOptions.Value;
            _json = jsonOptions.Value;
        }

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
            _logger.LogInformation("InvoiceRequestConsumer subscribed to {Topic} (group {Group})", TopicName, groupId);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, string>? record = null;

                    try
                    {
                        record = consumer.Consume(stoppingToken);
                        if (record is null) continue;

                        await ProcessAsync(record, stoppingToken).ConfigureAwait(false);

                        if (!_kafka.EnableAutoCommit)
                            consumer.Commit(record);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (PermanentMessageException pmx)
                    {
                        _logger.LogWarning(pmx, "Permanent failure; DLQ then commit.");
                        try { await PublishDlqAsync(record, pmx.Reason, stoppingToken).ConfigureAwait(false); }
                        catch (Exception dlqEx) { _logger.LogError(dlqEx, "DLQ publish failed for topic {Topic}", TopicName); }

                        if (record is not null && !_kafka.EnableAutoCommit)
                            consumer.Commit(record);
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

        // ------------------------------- Core processing --------------------------------

        private async Task ProcessAsync(ConsumeResult<string, string> record, CancellationToken ct)
        {
            var raw = record.Message?.Value;
            if (string.IsNullOrWhiteSpace(raw))
                throw Permanent("Empty payload");

            // Headers (case-insensitive convenience map)
            var headers = ToHeaderMap(record.Message?.Headers);

            var correlationId = FirstNonEmpty(
                GetHeader(headers, "correlation_id"),
                GetHeader(headers, "x-correlation-id"),
                Guid.NewGuid().ToString("N"));

            var idempKey = FirstNonEmpty(
                GetHeader(headers, "idempotency_key"),
                GetHeader(headers, "idempotency-key"),
                correlationId); // fallback: correlation

            var apiKeyText = GetHeader(headers, "api_key"); // REQUIRED
            var siteName = FirstNonEmpty(GetHeader(headers, "site_name"), GetHeader(headers, "x-site"));
            var companyId = FirstNonEmpty(GetHeader(headers, "company_id"), GetHeader(headers, "x-company"));

            // Resolve ApiKey
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var apiKey = await db.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Key == apiKeyText && k.IsActive, ct)
                .ConfigureAwait(false);

            if (apiKey is null)
                throw Permanent("Invalid or disabled API key");

            // Compute idempotency hash (SHA-512 base64 -> 88 chars)
            var idempHash = ComputeSha512Base64(idempKey);

            // Create a TransactionAttempt (Received)
            var attempt = new TransactionAttempt
            {
                CorrelationId = correlationId,
                IdempotencyKeyHash = idempHash,
                ApiKeyId = apiKey.Id,
                ProcessingStatus = "Received",
                ProcessingStartedUtc = DateTime.UtcNow,
                ResultMessage = "Inbound invoice request received from Kafka."
            };

            db.Set<TransactionAttempt>().Add(attempt);

            // Ensure an IdempotencyRecord exists (no-op if already there)
            await EnsureIdempotencyRecordAsync(db, idempHash, ct).ConfigureAwait(false);

            // Deserialize message body
            MdmInvoiceMessage message;
            try
            {
                message = JsonSerializer.Deserialize<MdmInvoiceMessage>(raw, _json)
                          ?? throw new JsonException("Deserialized null MdmInvoiceMessage");
            }
            catch (Exception jex)
            {
                attempt.ProcessingStatus = "SageFailure";
                attempt.ResultMessage = $"Invalid request JSON: {jex.Message}";
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                throw Permanent("Invalid request JSON");
            }

            // Build RequestContext (with defaulting; if missing headers, appsettings defaults will be used downstream)
            var requestContext = new RequestContext
            {
                SiteId = siteName,
                CompanyId = companyId,
                IdempotencyKey = idempKey,
                CorrelationId = correlationId
            };

            // Orchestrator handoff (File 3 will provide IInvoiceRequestOrchestrator)
            var orchestrator = scope.ServiceProvider.GetService<IInvoiceRequestOrchestrator>();
            if (orchestrator is null)
            {
                attempt.ProcessingStatus = "SageFailure";
                attempt.ResultMessage = "Orchestrator not registered.";
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                throw Permanent("Orchestrator not registered");
            }

            try
            {
                await orchestrator.OrchestrateAsync(message, requestContext, apiKey.Id, ct).ConfigureAwait(false);

                // Don't mark success here — the orchestrator will interact with Sage, update DB,
                // and publish results to MDM_INVOICE_RESULTS. Result consumers will finalize the attempt.
                attempt.ResultMessage = "Orchestration started.";
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (PermanentMessageException) { throw; }
            catch (Exception ex)
            {
                attempt.ProcessingStatus = "SageFailure";
                attempt.ResultMessage = $"Orchestration error: {ex.Message}";
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                throw; // let the loop treat as transient (no commit) unless it’s permanent
            }
        }

        // ------------------------------- Helpers --------------------------------

        private static Dictionary<string, string> ToHeaderMap(Headers? headers)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers is null) return map;

            foreach (var h in headers)
            {
                try
                {
                    map[h.Key] = Encoding.UTF8.GetString(h.GetValueBytes());
                }
                catch
                {
                    // ignore non-UTF8 header values
                }
            }
            return map;
        }

        private static string? GetHeader(Dictionary<string, string> headers, string name)
            => headers.TryGetValue(name, out var v) ? v?.Trim() : null;

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        private static string ComputeSha512Base64(string input)
        {
            using var sha = SHA512.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash); // 88 chars
        }

        private static AutoOffsetReset ParseOffsetReset(string? setting)
            => string.Equals(setting, nameof(AutoOffsetReset.Latest), StringComparison.OrdinalIgnoreCase)
                ? AutoOffsetReset.Latest
                : AutoOffsetReset.Earliest;

        private static PermanentMessageException Permanent(string reason) => new(reason);

        private static async Task EnsureIdempotencyRecordAsync(ApplicationContext db, string keyHash, CancellationToken ct)
        {
            var exists = await db.IdempotencyRecords
                .AsNoTracking()
                .AnyAsync(x => x.KeyHash == keyHash, ct)
                .ConfigureAwait(false);

            if (!exists)
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    KeyHash = keyHash,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        // -------------------- Permanent error for DLQ routing --------------------

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

        private async Task PublishDlqAsync(ConsumeResult<string, string>? record, string reason, CancellationToken ct)
        {
            var headers = record?.Message?.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var envelope = new
            {
                correlationId = record?.Message?.Key,
                reason,
                originalPayload = record?.Message?.Value ?? string.Empty,
                headers,
                occurredUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            });

            await _publisher.PublishAsync(DlqTopicName, json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Request orchestrator contract (implemented in File 3).
    /// </summary>
    public interface IInvoiceRequestOrchestrator
    {
        /// <summary>
        /// Performs customer upsert, creates SOP order, and kicks off invoice generation in Sage,
        /// logging to the microservice DB and publishing a result to MDM_INVOICE_RESULTS.
        /// Final status update is handled by the result consumer.
        /// </summary>
        Task OrchestrateAsync(MdmInvoiceMessage message, RequestContext context, int apiKeyId, CancellationToken ct);
    }
}
