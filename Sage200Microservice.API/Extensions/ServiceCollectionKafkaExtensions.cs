using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Messaging.Consumers;
using Sage200Microservice.Services.Messaging.Consumers.Results;

namespace Sage200Microservice.API.Extensions
{
    /// <summary>
    /// DI helpers to configure Kafka producer and register IEventPublisher behind feature flags.
    /// </summary>
    public static class ServiceCollectionKafkaExtensions
    {
        /// <summary>
        /// Registers Kafka options, producer, and event publisher based on feature flags.
        /// </summary>
        public static IServiceCollection AddKafkaPublisher(this IServiceCollection services, IConfiguration config)
        {
            var kafkaEnabled = config.GetValue("Features:Kafka:Enabled", false);

            services.AddOptions<KafkaOptions>().Bind(config.GetSection("Kafka"));

            if (kafkaEnabled)
            {
                services.AddSingleton<IProducer<string?, string>>(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;

                    var cfg = new ProducerConfig
                    {
                        BootstrapServers = opts.BootstrapServers ?? "localhost:9092",
                        ClientId = string.IsNullOrWhiteSpace(opts.ClientId)
                            ? $"sage200-microservice-{Environment.MachineName}"
                            : opts.ClientId,
                        EnableIdempotence = opts.EnableIdempotence
                    };

                    if (!string.IsNullOrWhiteSpace(opts.Acks) && Enum.TryParse<Acks>(opts.Acks, true, out var acks))
                        cfg.Acks = acks;
                    else
                        cfg.Acks = Acks.All;

                    if (!string.IsNullOrWhiteSpace(opts.SecurityProtocol) &&
                        Enum.TryParse<SecurityProtocol>(opts.SecurityProtocol, true, out var sec))
                        cfg.SecurityProtocol = sec;

                    if (!string.IsNullOrWhiteSpace(opts.SaslMechanism) &&
                        Enum.TryParse<SaslMechanism>(opts.SaslMechanism, true, out var mech))
                        cfg.SaslMechanism = mech;

                    if (!string.IsNullOrWhiteSpace(opts.SaslUsername))
                        cfg.SaslUsername = opts.SaslUsername;
                    if (!string.IsNullOrWhiteSpace(opts.SaslPassword))
                        cfg.SaslPassword = opts.SaslPassword;

                    if (opts.MessageTimeoutMs.HasValue) cfg.MessageTimeoutMs = opts.MessageTimeoutMs.Value;
                    if (opts.LingerMs.HasValue) cfg.LingerMs = opts.LingerMs.Value;
                    if (opts.BatchSize.HasValue) cfg.BatchSize = opts.BatchSize.Value;
                    if (!string.IsNullOrWhiteSpace(opts.CompressionType) &&
                        Enum.TryParse<CompressionType>(opts.CompressionType, true, out var comp))
                        cfg.CompressionType = comp;
                    if (opts.MessageSendMaxRetries.HasValue) cfg.MessageSendMaxRetries = opts.MessageSendMaxRetries.Value;
                    if (opts.RetryBackoffMs.HasValue) cfg.RetryBackoffMs = opts.RetryBackoffMs.Value;

                    return new ProducerBuilder<string?, string>(cfg).Build();
                });

                // Wrap the producer so we can test without a broker
                services.AddSingleton<IKafkaProducer, ConfluentKafkaProducer>();
                services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
            }
            else
            {
                services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
            }

            return services;
        }


        /// <summary>
        /// Registers Kafka consumer abstractions and implementations based on feature flags.
        /// Background services (the actual consumers) should be registered separately if enabled.
        /// </summary>
        public static IServiceCollection AddKafkaConsumers(this IServiceCollection services, IConfiguration config)
        {
            var kafkaEnabled = config.GetValue("Features:Kafka:Enabled", false);
            if (!kafkaEnabled)
            {
                // If Kafka is disabled globally, don't register consumers
                return services;
            }

            // Ensure options are configured (likely already done by AddKafkaPublisher, but safe to repeat)
            services.AddOptions<KafkaOptions>().Bind(config.GetSection("Kafka"));

            // Register the Confluent Consumer configuration logic.
            // Use Scoped or Transient: Each consumer BackgroundService often needs its own instance. Transient is safer.
            services.AddTransient<IConsumer<Ignore, string>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<ConfluentKafkaConsumer>>(); // Use logger for consumer

                var config = new ConsumerConfig
                {
                    BootstrapServers = opts.BootstrapServers ?? "localhost:9092",
                    GroupId = opts.ConsumerGroupId ?? "sage200-microservice-default-group",
                    // ... copy relevant security, ClientId, AutoOffsetReset, EnableAutoCommit, MaxPollIntervalMs etc from ConfluentKafkaConsumer constructor logic ...
                    EnableAutoCommit = opts.EnableAutoCommit
                };
                // Add Error/Stats handlers similar to ConfluentKafkaConsumer constructor if desired globally
                return new ConsumerBuilder<Ignore, string>(config).Build();
            });

            // Register our abstraction
            services.AddTransient<IKafkaConsumer, ConfluentKafkaConsumer>();

            services.AddHostedService<SalesInvoiceCreateConsumer>();
            services.AddHostedService<InvoiceResultConsumer>();
            services.AddHostedService<CustomerResultConsumer>();
            services.AddHostedService<SopResultConsumer>();

            return services;
        }
    }
}
