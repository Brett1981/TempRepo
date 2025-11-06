// <PackageReference Include="Confluent.Kafka" Version="2.*" />
using Confluent.Kafka;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Kafka-backed publisher using Confluent.Kafka.
    /// </summary>
    public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
    {
        private readonly IProducer<string?, string> _producer;
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public KafkaEventPublisher(IProducer<string?, string> producer)
            => _producer = producer ?? throw new ArgumentNullException(nameof(producer));

        /// <summary>Fire-and-forget enqueue.</summary>
        public void Publish(string topic, object payload)
        {
            var value = JsonSerializer.Serialize(payload, _json);
            _producer.Produce(topic, new Message<string, string> { Value = value });
        }
        /// <summary>Await the broker ack.</summary>
        public async Task PublishAsync(string topic, object payload, CancellationToken cancellationToken = default)
        {
            var value = JsonSerializer.Serialize(payload, _json);
            _ = await _producer.ProduceAsync(topic, new Message<string?, string> { Key = null, Value = value }, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _producer.Flush(TimeSpan.FromSeconds(5)); // Flush producer on dispose
    }
}
