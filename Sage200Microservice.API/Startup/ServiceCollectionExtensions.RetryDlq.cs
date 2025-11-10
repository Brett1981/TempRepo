using Microsoft.Extensions.DependencyInjection;
using Sage200Microservice.Services.Data;
using Sage200Microservice.Services.Messaging.Publishing;


namespace Sage200Microservice.API.Startup
{
    public static class ServiceCollectionExtensionsRetryDlq
    {
        public static IServiceCollection AddRetryDlqParity(this IServiceCollection services)
        {
            services.AddScoped<ITransactionAttemptLogger, TransactionAttemptLogger>();
            services.AddScoped<DlqPublisher>();
            // IKafkaProducer must be bound to the concrete Kafka producer used by the host application.
            return services;
        }
    }
}