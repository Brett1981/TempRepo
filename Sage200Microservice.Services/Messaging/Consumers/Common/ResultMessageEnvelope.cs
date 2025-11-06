// -----------------------------------------------------------------------------------------------------
// Purpose:
//   Canonical, minimal, and resilient deserialization model for inbound Kafka result messages from the
//   topics: MDM_INVOICE_RESULTS, MDM_CUSTOMER_RESULTS, MDM_SOP_RESULTS.
//
// Notes:
//   • Field names use snake_case to match upstream payloads.
//   • Optional members are nullable; consumers must degrade gracefully if absent.
//   • This envelope is intentionally neutral (no domain coupling) so all result-consumers can share it.
//   • Status is represented as a string; consumers will map to TransactionAttempt status enums.
//   • “errors” is a collection of simple code/message pairs (optional).
//
// Serialization policy:
//   • System.Text.Json with [JsonPropertyName] per property to ensure strict field mapping.
//   • Consumers should configure JsonSerializerOptions with DefaultIgnoreCondition = WhenWritingNull
//     and NEVER emit explicit nulls when producing (aligns with your “omit nulls” rule).
// =====================================================================================================
namespace Sage200Microservice.Services.Messaging.Consumers.Common
{
    using Sage200Microservice.Data.Models;
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents the minimal, stable envelope for result events published to Kafka result topics.
    /// This DTO is used by all result consumers to deserialize inbound messages before applying
    /// mapping logic into TransactionAttempts, ExternalIdLink, and AuditLogs.
    /// </summary>
    public sealed class ResultMessageEnvelope
    {
        /// <summary>
        /// Correlation identifier that ties this result back to the originating request/attempt.
        /// Preferred partition key. May be null in some upstream edge cases.
        /// </summary>
        [JsonPropertyName("correlationId")]
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Idempotency key originally supplied at request time. Used as a fallback match when
        /// the correlation id is missing. May be plain text; consumers may hash if required to match schema.
        /// </summary>
        [JsonPropertyName("idempotencyKey")]
        public string? IdempotencyKey { get; set; }

        /// <summary>
        /// External system reference for the entity (e.g., client’s unique id). When present, consumers
        /// should upsert ExternalIdLink using AppId, ExternalRef, EntityType, SageUrn/SageId.
        /// </summary>
        [JsonPropertyName("externalRef")]
        public string? ExternalRef { get; set; }

        /// <summary>
        /// Optional upstream-resolved application id (ApiKeys.Id). If not supplied, consumers should
        /// fall back to TransactionAttempt.ApiKeyId when upserting ExternalIdLink and writing AuditLogs.
        /// </summary>
        [JsonPropertyName("apiKeyId")]
        public int? ApiKeyId { get; set; }

        /// <summary>
        /// Entity type that this result relates to. Allowed values (per contract):
        /// "Invoice", "Customer", "SopOrder".
        /// </summary>
        [JsonPropertyName("entityType")]
        public ExternalEntityType? EntityType { get; set; }

        /// <summary>
        /// Result status string from upstream. Allowed values:
        /// "Success" or "Failure". Consumers convert to TransactionAttempts status enum values.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// Sage URN returned by Sage 200 for the processed entity. Persisted to TransactionAttempts.SageUrn
        /// and mirrored to ExternalIdLink when applicable.
        /// </summary>
        [JsonPropertyName("sageUrn")]
        public string? SageUrn { get; set; }

        /// <summary>
        /// Optional Sage numeric identifier. Persisted alongside URN where supplied.
        /// </summary>
        [JsonPropertyName("sageId")]
        public int? SageId { get; set; }

        /// <summary>
        /// Optional structured error list provided by upstream on failure.
        /// Consumers should pick a concise, masked message for TransactionAttempts.ResultMessage and AuditLogs.
        /// </summary>
        [JsonPropertyName("errors")]
        public List<ResultErrorItem>? Errors { get; set; }

        /// <summary>
        /// Optional upstream receive timestamp. If absent, consumers should use DateTime.UtcNow
        /// for TransactionAttempts.ProcessingCompletedUtc.
        /// </summary>
        [JsonPropertyName("receivedAtUtc")]
        public DateTimeOffset? ReceivedAtUtc { get; set; }

        /// <summary>
        /// Optional end-to-end processing duration in milliseconds as reported by upstream.
        /// If absent, consumers may derive a duration from the attempt's start time.
        /// </summary>
        [JsonPropertyName("durationMs")]
        public long? DurationMs { get; set; }

        /// <summary>
        /// Produces a concise, masked message suitable for persisting into TransactionAttempts.ResultMessage
        /// and AuditLogs, favouring the first error if available.
        /// </summary>
        public string ToConciseResultMessage(string topicName)
        {
            // Defensive defaults
            var status = string.IsNullOrWhiteSpace(Status) ? "Unknown" : Status!;
            var urnPart = string.IsNullOrWhiteSpace(SageUrn) ? string.Empty : $" URN={SageUrn}";
            if (Errors is { Count: > 0 })
            {
                var first = Errors[0];
                var code = string.IsNullOrWhiteSpace(first.Code) ? "ERR" : first.Code!;
                var msg = string.IsNullOrWhiteSpace(first.Message) ? "Error" : first.Message!;
                // Keep it short; masking/redaction should be handled upstream where needed.
                return $"Result {status} from {topicName}.{urnPart} Error={code}:{TrimForLog(msg, 256)}";
            }

            return $"Result {status} from {topicName}.{urnPart}".Trim();
        }

        /// <summary>
        /// Helper that trims a string to a maximum length, safe for log/messages.
        /// </summary>
        private static string TrimForLog(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLen ? value : value.Substring(0, maxLen);
        }
    }

    /// <summary>
    /// Represents a single error item in the inbound result envelope.
    /// </summary>
    public sealed class ResultErrorItem
    {
        /// <summary>
        /// Upstream error code (may be null/empty).
        /// </summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>
        /// Upstream error message (may be null/empty). Consumers should avoid persisting sensitive data.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}