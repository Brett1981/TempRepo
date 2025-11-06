using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Orchestrates an inbound invoice request coming from Kafka.
    /// </summary>
    public interface IInvoiceRequestOrchestrator
    {
        /// <summary>
        /// Processes a single invoice request from Kafka:
        /// Upsert customer → create SOP order → optionally generate invoice → log + publish result.
        /// </summary>
        Task OrchestrateAsync(
            MdmInvoiceMessage message,
            RequestContext context,
            int apiKeyId,
            CancellationToken ct);
    }
}
