using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Sage200Microservice.Services.Data;
using Sage200Microservice.Services.Messaging.Publishing;


namespace Sage200Microservice.API.Startup
{
    public static class ServiceCollectionExtensionsRetryDlq
    {
        public static IServiceCollection AddRetryDlqParity(this IServiceCollection services)
        {
            services.AddScoped<Sage200Microservice.Services.Data.ITransactionAttemptLogger, Sage200Microservice.Services.Data.TransactionAttemptLogger>();
            services.AddScoped<DlqPublisher>();
            // IKafkaProducer is bound in AddKafkaProducer(...) below.
            return services;
        }

        /// <summary>
        /// Registers the Confluent Kafka producer and binds IKafkaProducer to our implementation.
        /// </summary>
        public static IServiceCollection AddKafkaProducer(this IServiceCollection services, IConfiguration configuration)
        {
            var kafka = configuration.GetSection("Kafka");
            var bootstrap = kafka["BootstrapServers"];
            if (string.IsNullOrWhiteSpace(bootstrap))
            {
                throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");
            }

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrap,
                Acks = Acks.Leader,         // or Acks.All if you want stronger guarantees
                EnableIdempotence = true,   // safe defaults for reliability
                LingerMs = 5,
                MessageSendMaxRetries = 3,
                BatchSize = 32 * 1024
            };

            // Confluent producer as a singleton, disposed by the container on shutdown
            services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(producerConfig).Build());

            // Bind our abstraction to the Confluent-backed implementation
            services.AddSingleton<IKafkaProducer, ConfluentKafkaProducer>();

            return services;
        }
    }
}