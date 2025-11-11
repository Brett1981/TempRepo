using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace Sage200Microservice.Services.Messaging.Publishing
{
    /// <summary>
    /// Confluent-backed implementation of IKafkaProducer.
    /// </summary>
    public sealed class ConfluentKafkaProducer : IKafkaProducer
    {
        private readonly IProducer<string, string> _producer;

        public ConfluentKafkaProducer(IProducer<string, string> producer)
        {
            _producer = producer;
        }

        public async Task ProduceAsync(string topic, string key, string value, CancellationToken cancellationToken)
        {
            var msg = new Message<string, string> { Key = key, Value = value };
            // ConfigureAwait(false) is safe here
            _ = await _producer.ProduceAsync(topic, msg, cancellationToken).ConfigureAwait(false);
        }
    }
}
