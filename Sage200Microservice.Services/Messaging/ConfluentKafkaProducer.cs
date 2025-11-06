using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// IKafkaProducer implementation that wraps Confluent.Kafka IProducer.
    /// </summary>
    public sealed class ConfluentKafkaProducer : IKafkaProducer, IDisposable
    {
        private readonly ILogger<ConfluentKafkaProducer> _log;
        private readonly IProducer<string?, string> _producer;

        public ConfluentKafkaProducer(IProducer<string?, string> producer, ILogger<ConfluentKafkaProducer> log)
        {
            _producer = producer;
            _log = log;
        }

        /// <summary>
        /// Produces a message and logs delivery results. Uses ProduceAsync which throws ProduceException on failures.
        /// </summary>
        public async Task ProduceAsync(string topic, string value, Headers headers, CancellationToken ct)
        {
            try
            {
                var msg = new Message<string?, string>
                {
                    Key = null,
                    Value = value,
                    Headers = headers
                };

                var dr = await _producer.ProduceAsync(topic, msg, ct).ConfigureAwait(false);

                if (dr.Status == PersistenceStatus.Persisted)
                {
                    _log.LogInformation("Kafka delivered to {TPO}", dr.TopicPartitionOffset);
                }
                else
                {
                    _log.LogError("Kafka delivery not persisted. Status={Status}, Topic={Topic}, Partition={Partition}, Offset={Offset}",
                        dr.Status, dr.Topic, dr.Partition, dr.Offset);
                }
            }
            catch (ProduceException<string?, string> pex)
            {
                _log.LogError(pex, "Kafka produce error to {Topic}: {Reason}", topic, pex.Error.Reason);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected Kafka publish failure to topic {Topic}.", topic);
                throw;
            }
        }

        public void Dispose()
        {
            try { _producer.Flush(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
            _producer.Dispose();
        }
    }
}
