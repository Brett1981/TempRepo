using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// No-operation publisher; safe default for DEV/UAT without Kafka.
    /// </summary>
    public sealed class NoOpEventPublisher : IEventPublisher
    {
        public void Publish(string topic, object payload) { /* intentionally empty */ }

        public Task PublishAsync(string topic, object payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
