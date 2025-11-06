using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Kafka-friendly event publishing seam. Implementations can be NoOp or Kafka-backed.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// Legacy synchronous publish (optional for simple implementations).
        /// </summary>
        void Publish(string topic, object payload);

        /// <summary>
        /// Asynchronously publishes a structured payload to a topic.
        /// Default implementation bridges to the sync method.
        /// </summary>
        Task PublishAsync(string topic, object payload, CancellationToken cancellationToken = default)
            => Task.Run(() => Publish(topic, payload), cancellationToken);
    }
}
