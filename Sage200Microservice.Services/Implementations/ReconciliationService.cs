using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories; // Keep for IApiKeyRepository (if needed elsewhere, though not for AppId lookup)
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Common;
using Sage200Microservice.Services.Models.Reconciliation;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Service responsible for querying locally stored, reconciled Sage data
    /// linked via ExternalIdLink, scoped by Application ID (AppId derived via ApiKeyId).
    /// </summary>
    public class ReconciliationService : IReconciliationService
    {
        private readonly ILogger<ReconciliationService> _logger;
        private readonly ApplicationContext _dbContext;
        // Removed IApiKeyRepository as it's not needed for AppId lookup anymore in this method

        public ReconciliationService(
            ILogger<ReconciliationService> logger,
            ApplicationContext dbContext
            /* Removed IApiKeyRepository dependency */ )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            // _apiKeyRepository removed
        }

        /// <summary>
        /// Gets paginated reconciliation data based on query parameters, scoped by ApiKeyId via ExternalIdLink.AppId.
        /// </summary>
        public async Task<PaginatedDataResponse<ReconciliationDataDto>> GetReconciliationDataAsync(
            ReconciliationQueryParameters queryParams,
            int apiKeyId, // Passed from Controller, used directly for scoping
            CancellationToken ct)
        {
            _logger.LogInformation("GetReconciliationDataAsync called for ApiKeyId {ApiKeyId}. Filters: ExternalRef={ExternalRef}, SageUrn={SageUrn}, EntityType={EntityType}, Page={Page}, Size={Size}",
                apiKeyId, queryParams.ExternalRef, queryParams.SageUrn, queryParams.EntityType, queryParams.PageNumber, queryParams.PageSize);

            // 1. Define AppId for Query (it IS the ApiKeyId)
            int appIdForQuery = apiKeyId; // ExternalIdLink.AppId == ApiKey.Id
            _logger.LogDebug("Scoping reconciliation query to AppId (ApiKeyId): {AppIdForQuery}", appIdForQuery);

            // 2. Build Base Query - START WITH ExternalIdLink scoped by AppId (which is apiKeyId)
            IQueryable<ExternalIdLink> linkQuery = _dbContext.ExternalIdLinks
                                                        .AsNoTracking()
                                                        .Where(link => link.AppId == appIdForQuery); // <<< SCOPING HERE using apiKeyId

            // 3. Apply Filters (Prioritize SageUrn)
            bool appliedFilter = false;
            if (!string.IsNullOrWhiteSpace(queryParams.SageUrn))
            {
                linkQuery = linkQuery.Where(link => link.SageUrn == queryParams.SageUrn);
                appliedFilter = true;
                _logger.LogDebug("Applying SageUrn filter: {SageUrn}", queryParams.SageUrn);
            }
            else if (!string.IsNullOrWhiteSpace(queryParams.ExternalRef))
            {
                // AppId filter is already applied
                linkQuery = linkQuery.Where(link => link.ExternalRef == queryParams.ExternalRef);
                appliedFilter = true;
                _logger.LogDebug("Applying ExternalRef filter: {ExternalRef} (for AppId {AppIdForQuery})", queryParams.ExternalRef, appIdForQuery);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.EntityType))
            {
                if (Enum.TryParse<ExternalEntityType>(queryParams.EntityType, true, out var parsedEntityType))
                {
                    linkQuery = linkQuery.Where(link => link.EntityType == parsedEntityType);
                    appliedFilter = true;
                    _logger.LogDebug("Applying EntityType filter: {EntityType}", parsedEntityType);
                }
                else
                {
                    _logger.LogWarning("Invalid EntityType filter provided: {EntityType}", queryParams.EntityType);
                    return CreateEmptyPaginatedResponse(queryParams);
                }
            }

            if (!appliedFilter && string.IsNullOrWhiteSpace(queryParams.ExternalRef) && string.IsNullOrWhiteSpace(queryParams.SageUrn))
            {
                _logger.LogWarning("GetReconciliationDataAsync requires either ExternalRef or SageUrn filter for AppId {AppIdForQuery}", appIdForQuery);
                return CreateEmptyPaginatedResponse(queryParams);
            }

            // 4. Placeholder Join & Projection (Corrected DTO property usage)
            //    SELECT directly from ExternalIdLink for Stage 9.
            //    Stage 10 will add JOINs to SagePayments/SageAllocations DbSets.
            var projectedQuery = linkQuery.Select(link => new ReconciliationDataDto
            {
                // --- Fields from ExternalIdLink (Available Now) ---
                AppId = link.AppId, // Which is the apiKeyId
                ExternalRef = link.ExternalRef,
                EntityType = link.EntityType.ToString(),
                SageTransactionUrn = link.SageUrn,
                SageId = link.SageId,
                LinkCreatedAtUtc = link.CreatedUtc,

                // --- Fields from SagePayments/SageAllocations (PLACEHOLDERS for Stage 10) ---
                TransactionDate = null,
                Amount = null,
                CurrencyCode = null,
                SageReference = null,
                SageAccountReference = null,
                AllocationDetails = null
            });

            // 5. Get Total Count
            int totalCount = await projectedQuery.CountAsync(ct);
            _logger.LogDebug("Total matching records found: {TotalCount}", totalCount);

            // 6. Apply Pagination & Execute Query
            var pagedQuery = projectedQuery
                                .OrderBy(dto => dto.ExternalRef) // Default sort
                                .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
                                .Take(queryParams.PageSize);

            List<ReconciliationDataDto> items = await pagedQuery.ToListAsync(ct);
            _logger.LogDebug("Retrieved {ItemCount} records for page {PageNumber}", items.Count, queryParams.PageNumber);

            // 7. Return Paginated Response
            var metadata = new PaginationMetadata
            {
                CurrentPage = queryParams.PageNumber,
                PageSize = queryParams.PageSize,
                TotalCount = totalCount
            };

            return new PaginatedDataResponse<ReconciliationDataDto>
            {
                Metadata = metadata,
                Items = items
            };
        }

        // Helper to create an empty response
        private PaginatedDataResponse<ReconciliationDataDto> CreateEmptyPaginatedResponse(ReconciliationQueryParameters queryParams)
        {
            return new PaginatedDataResponse<ReconciliationDataDto>
            {
                Metadata = new PaginationMetadata
                {
                    CurrentPage = queryParams.PageNumber,
                    PageSize = queryParams.PageSize,
                    TotalCount = 0
                },
                Items = new List<ReconciliationDataDto>()
            };
        }
    }
}

