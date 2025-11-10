using System;
using System.Threading;
using System.Threading.Tasks;


namespace Sage200Microservice.Services.Data
{
    public interface ITransactionAttemptLogger
    {
        Task<long> StartAttemptAsync(string correlationId, string topic, int partition, long offset, int attemptNumber, CancellationToken ct);
        Task CompleteAttemptAsync(long attemptId, bool success, string? resultMessage, TimeSpan duration, CancellationToken ct);
    }
}