using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Services.Data;
using Sage200Microservice.Services.Messaging.Contracts;
using Sage200Microservice.Services.Messaging.Policies;
using Sage200Microservice.Services.Messaging.Publishing;


namespace Sage200Microservice.Services.Processing
{
    /// <summary>
    /// Helper to execute a consumer handler with standard retry/DLQ behavior and attempt logging.
    /// Call this from each Kafka consumer when processing a message.
    /// </summary>
    public sealed class ConsumerExecutionWrapper
    {
        private readonly ITransactionAttemptLogger _attemptLogger;
        private readonly DlqPublisher _dlqPublisher;


        public ConsumerExecutionWrapper(ITransactionAttemptLogger attemptLogger, DlqPublisher dlqPublisher)
        {
            _attemptLogger = attemptLogger;
            _dlqPublisher = dlqPublisher;
        }


        public async Task ExecuteAsync(
        string correlationId,
        string entityType,
        string originalTopic,
        string dlqTopic,
        int partition,
        long offset,
        string originalPayload,
        Func<CancellationToken, Task> handler,
        Func<Exception, bool> isTransient,
        CancellationToken ct)
        {
            var sw = new Stopwatch();
            long lastAttemptId = 0;
            int attempt = 0;


            bool success = await RetryPolicyFactory.ExecuteWithRetryAsync(
            async token =>
            {
                attempt++;
                sw.Restart();
                lastAttemptId = await _attemptLogger.StartAttemptAsync(
    correlationId, originalTopic, partition, offset, attempt, token);


                await handler(token).ConfigureAwait(false);
                sw.Stop();


                await _attemptLogger.CompleteAttemptAsync(lastAttemptId, success: true, resultMessage: null, duration: sw.Elapsed, token).ConfigureAwait(false);
            },
            isTransient: isTransient,
            onRetry: (n, ex) => { /* metrics incremented inside policy */ },
            cancellationToken: ct).ConfigureAwait(false);


            if (!success)
            {
                sw.Stop();
                if (lastAttemptId != 0)
                {
                    await _attemptLogger.CompleteAttemptAsync(lastAttemptId, success: false, resultMessage: "Retries exhausted", duration: sw.Elapsed, ct).ConfigureAwait(false);
                }


                var envelope = new DlqEnvelope
                {
                    CorrelationId = correlationId,
                    OriginalTopic = originalTopic,
                    EntityType = entityType,
                    ExternalReference = null,
                    ErrorCategory = "Transient",
                    ErrorMessage = "Exceeded retry attempts (10s, 30s, 2m)",
                    StackTrace = null,
                    OriginalPayload = originalPayload
                };


                await _dlqPublisher.PublishAsync(envelope, dlqTopic, ct).ConfigureAwait(false);
                // NOTE: Commit of the consumer offset should occur in the caller after PublishAsync succeeds.
            }
        }
    }
}