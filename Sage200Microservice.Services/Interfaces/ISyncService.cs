using Sage200Microservice.Services.Models; // For RequestContext
using Sage200Microservice.Services.Models.Sync;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service responsible for handling background synchronization triggers with Sage.
    /// </summary>
    public interface ISyncService
    {
        /// <summary>
        /// Initiates a background synchronization process based on the request.
        /// This method should be lightweight, queueing the actual work and returning quickly.
        /// It is responsible for creating the initial TransactionAttempts record.
        /// </summary>
        /// <param name="request">Details of what to sync.</param>
        /// <param name="context">Request context containing headers, correlation ID. ApiKeyId/AppId are NOT included here.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A response indicating the request was accepted, including the CorrelationId.</returns>
        Task<SyncInitiatedResponse> RequestSyncAsync(FetchSageUpdatesRequest request, RequestContext context, CancellationToken ct);
    }
}

