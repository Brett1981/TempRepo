using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.Services.Messaging.Contracts;   // DlqEnvelope
using Sage200Microservice.Services.Messaging.Publishing;  // IKafkaProducer

namespace Sage200Microservice.API.Controllers.Admin
{
    [ApiController]
    [Route("api/kafka/replay")]
    [Authorize(Roles = "Admin")] // Admin gating
    public sealed class KafkaReplayController : ControllerBase
    {
        private readonly DlqPublisher _dlqPublisher; // kept for future use (e.g., re-DLQ) even if unused now
        private readonly IKafkaProducer _producer;

        public KafkaReplayController(DlqPublisher dlqPublisher, IKafkaProducer producer)
        {
            _dlqPublisher = dlqPublisher;
            _producer = producer;
        }

        /// <summary>
        /// Accepts a DLQ payload and republishes the original message to its original topic (or an override).
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReplayAsync([FromBody] ReplayRequestDto request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Telemetry
            Sage200Microservice.Data.Telemetry.Metrics.ReplayRequestsTotal.Add(1);

            // If body is a DlqEnvelope, use OriginalPayload; otherwise treat body as the raw original payload.
            string originalPayload;
            try
            {
                var dlq = JsonSerializer.Deserialize<DlqEnvelope>(request.DlqPayloadJson);
                originalPayload = dlq?.OriginalPayload ?? request.DlqPayloadJson;
            }
            catch
            {
                originalPayload = request.DlqPayloadJson;
            }

            var targetTopic = string.IsNullOrWhiteSpace(request.TargetTopicOverride)
                ? request.OriginalTopic
                : request.TargetTopicOverride!;

            await _producer.ProduceAsync(targetTopic, request.CorrelationId, originalPayload, ct).ConfigureAwait(false);

            return Accepted(new
            {
                message = "Replay scheduled",
                correlationId = request.CorrelationId,
                topic = targetTopic
            });
        }
    }
}
