using Sage200Microservice.Services.Models.Common;
using Sage200Microservice.Services.Models.Reconciliation;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Interfaces
{
    /// <summary>
    /// Service responsible for querying reconciled Sage data stored locally.
    /// </summary>
    public interface IReconciliationService
    {
        /// <summary>
        /// Fetches paginated reconciliation data (e.g., Sage Payments, Allocations)
        /// linked via ExternalIdLink, filtered by the provided parameters and secured by ApiKeyId.
        /// </summary>
        /// <param name="queryParams">Filtering and pagination parameters.</param>
        /// <param name="apiKeyId">The ID of the authenticated API key, used for data scoping via ExternalIdLink.AppId.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A paginated list of reconciliation data DTOs.</returns>
        Task<PaginatedDataResponse<ReconciliationDataDto>> GetReconciliationDataAsync(
            ReconciliationQueryParameters queryParams,
            int apiKeyId, // Changed from appId to apiKeyId
            CancellationToken ct);

        // Add other reconciliation methods as needed in Stage 9+
    }
}

