using Confluent.Kafka;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Abstraction for a Kafka consumer client.
    /// </summary>
    public interface IKafkaConsumer : IDisposable
    {
        /// <summary>
        /// Subscribes the consumer to the specified topic(s).
        /// </summary>
        /// <param name="topics">The topic or list of topics to subscribe to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken);

        /// <summary>
        /// Consumes a single message from the subscribed topics. Blocks until a message is available
        /// or the cancellation token is triggered.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to interrupt consumption.</param>
        /// <returns>The consumed message result, or null if cancelled or timed out.</returns>
        Task<ConsumeResult<Ignore, string>?> ConsumeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Manually commits the offset for the provided consume result.
        /// Use when EnableAutoCommit is false.
        /// </summary>
        /// <param name="result">The result of a previous ConsumeAsync call.</param>
        Task CommitAsync(ConsumeResult<Ignore, string> result);

        /// <summary>
        /// Closes the consumer connection.
        /// </summary>
        void Close();
    }
}