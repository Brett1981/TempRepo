using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Messaging
{
    /// <summary>
    /// Small abstraction to allow unit testing of the publisher without a live Kafka broker.
    /// </summary>
    public interface IKafkaProducer
    {
        /// <summary>Produces a message to the given topic with headers.</summary>
        Task ProduceAsync(string topic, string value, Headers headers, CancellationToken ct);
    }
}
