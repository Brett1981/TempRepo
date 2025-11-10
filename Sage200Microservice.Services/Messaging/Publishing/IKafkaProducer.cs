using System.Threading;
using System.Threading.Tasks;


namespace Sage200Microservice.Services.Messaging.Publishing
{
    /// <summary>
    /// Minimal producer abstraction to decouple from specific Kafka client implementation.
    /// </summary>
    public interface IKafkaProducer
    {
        Task ProduceAsync(string topic, string key, string value, CancellationToken cancellationToken);
    }
}