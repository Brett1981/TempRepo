using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    /// <summary>
    /// Repository for ExternalIdLink CRUD/query operations.
    /// </summary>
    public interface IExternalIdLinkRepository
    {
        /// <summary>
        /// Try to insert a new mapping. Returns false if an identical mapping already exists.
        /// Throws <see cref="System.InvalidOperationException"/> if a different canonical mapping exists
        /// for the same (AppId, EntityType, ExternalRef). Conflict specifics are added to the exception message.
        /// </summary>
        Task<bool> TryInsertAsync(ExternalIdLink link, CancellationToken ct = default);

        /// <summary>
        /// Find a mapping by (AppId, EntityType, ExternalRef).
        /// </summary>
        Task<ExternalIdLink?> FindByExternalAsync(int appId, ExternalEntityType entityType, string externalRef, CancellationToken ct = default);

        /// <summary>
        /// Reverse lookup by canonical numeric id.
        /// </summary>
        Task<IReadOnlyList<ExternalIdLink>> ListBySageIdAsync(ExternalEntityType entityType, long sageId, int page = 1, int pageSize = 50, CancellationToken ct = default);

        /// <summary>
        /// Reverse lookup by canonical URN.
        /// </summary>
        Task<IReadOnlyList<ExternalIdLink>> ListBySageUrnAsync(ExternalEntityType entityType, string sageUrn, int page = 1, int pageSize = 50, CancellationToken ct = default);
    }
}
