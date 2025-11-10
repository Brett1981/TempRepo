using System;
using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Data.Telemetry;

namespace Sage200Microservice.Services.Messaging.Policies
{
    /// <summary>
    /// Provides the canonical retry sequence for transient failures: 10s, 30s, 120s (2 minutes).
    /// </summary>
    public static class RetryPolicyFactory
    {
        private static readonly TimeSpan[] Backoff = new[]
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2)
        };


        /// <summary>
        /// Executes the operation with the backoff policy. Returns true if the operation eventually succeeded.
        /// The classifier determines whether an exception is transient (retryable) or permanent.
        /// </summary>
        public static async Task<bool> ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        Func<Exception, bool> isTransient,
        Action<int, Exception>? onRetry,
        CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= Backoff.Length + 1; attempt++)
            {
                try
                {
                    await operation(cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex) when (isTransient(ex) && attempt <= Backoff.Length)
                {
                    onRetry?.Invoke(attempt, ex);
                    Metrics.RetriesTotal.Add(1);
                    await Task.Delay(Backoff[attempt - 1], cancellationToken).ConfigureAwait(false);
                }
            }
            return false;
        }
    }
}