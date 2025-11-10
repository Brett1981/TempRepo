using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Data.Telemetry;
using Sage200Microservice.Services.Messaging.Contracts;


namespace Sage200Microservice.Services.Messaging.Publishing
{
    public sealed class DlqPublisher
    {
        private readonly IKafkaProducer _producer;
        private const int MaxPayloadBytes = 10 * 1024; // 10KB safety cap


        public DlqPublisher(IKafkaProducer producer)
        {
            _producer = producer;
        }


        public async Task PublishAsync(DlqEnvelope envelope, string dlqTopic, CancellationToken ct)
        {
            var normalized = NormalizePayload(envelope);
            var payload = JsonSerializer.Serialize(normalized);
            await _producer.ProduceAsync(dlqTopic, normalized.CorrelationId, payload, ct).ConfigureAwait(false);
            Metrics.DlqMessagesTotal.Add(1);
        }


        private DlqEnvelope NormalizePayload(DlqEnvelope envelope)
        {
            // Ensure payload length cap to avoid oversized DLQ records
            var bytes = Encoding.UTF8.GetBytes(envelope.OriginalPayload ?? string.Empty);
            if (bytes.Length <= MaxPayloadBytes) return envelope;


            var truncated = Encoding.UTF8.GetString(bytes, 0, MaxPayloadBytes);
            return new DlqEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                OriginalTopic = envelope.OriginalTopic,
                EntityType = envelope.EntityType,
                ExternalReference = envelope.ExternalReference,
                ErrorCategory = envelope.ErrorCategory,
                ErrorMessage = envelope.ErrorMessage + " (payload truncated)",
                StackTrace = envelope.StackTrace,
                OriginalPayload = truncated,
                TimestampUtc = envelope.TimestampUtc
            };
        }
    }
}