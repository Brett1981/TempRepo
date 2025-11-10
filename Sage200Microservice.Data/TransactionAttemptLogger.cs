using Microsoft.EntityFrameworkCore;
using Sage200Microservice.Data; // DbContext from Data project


namespace Sage200Microservice.Services.Data
{
    /// <summary>
    /// Persists retry attempts and durations into TransactionAttempts table via ApplicationContext.
    /// </summary>
    public sealed class TransactionAttemptLogger : ITransactionAttemptLogger
    {
        private readonly ApplicationContext _db;


        public TransactionAttemptLogger(ApplicationContext db) => _db = db;


        public async Task<long> StartAttemptAsync(string correlationId, string topic, int partition, long offset, int attemptNumber, CancellationToken ct)
        {
            var attempt = new Sage200Microservice.Data.Models.TransactionAttempt // POCO in Data project expected
            {
                CorrelationId = correlationId,
                KafkaTopic = topic,
                KafkaPartition = partition,
                KafkaOffset = offset,
                AttemptNumber = attemptNumber,
                ProcessingStartedUtc = DateTime.UtcNow,
                ProcessingStatus = "InProgress"
            };


            _db.Set<Sage200Microservice.Data.Models.TransactionAttempt>().Add(attempt);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return attempt.Id; // assumes identity PK
        }


        public async Task CompleteAttemptAsync(long attemptId, bool success, string? resultMessage, TimeSpan duration, CancellationToken ct)
        {
            var attempt = await _db.Set<Sage200Microservice.Data.Models.TransactionAttempt>()
            .Where(a => a.Id == attemptId)
            .SingleAsync(ct)
            .ConfigureAwait(false);


            attempt.ProcessingCompletedUtc = DateTime.UtcNow;
            attempt.ProcessingStatus = success ? "SageSuccess" : "Failed";
            attempt.ResultMessage = resultMessage;
            attempt.DurationMs = (int?)(long)duration.TotalMilliseconds;


            if (!success)
            {
                Sage200Microservice.Data.Telemetry.Metrics.MessageFailuresTotal.Add(1);
            }


            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}