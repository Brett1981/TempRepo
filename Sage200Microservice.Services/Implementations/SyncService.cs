using Microsoft.AspNetCore.Http; // Added for IHttpContextAccessor
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Sync;
using System;
using System.ComponentModel.DataAnnotations; // Added for ValidationException
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Service responsible for handling sync requests, ensuring idempotency,
    /// and creating TransactionAttempt records for background processing.
    /// </summary>
    public class SyncService : ISyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ApplicationContext _dbContext;
        private readonly IIdempotencyRecordRepository _idempotencyRepo;
        private readonly IHttpContextAccessor _httpContextAccessor; // Added
        private readonly IApiKeyRepository _apiKeyRepository;     // Added

        public SyncService(
            ILogger<SyncService> logger,
            ApplicationContext dbContext,
            IIdempotencyRecordRepository idempotencyRepo,
            IHttpContextAccessor httpContextAccessor, // Added
            IApiKeyRepository apiKeyRepository)     // Added
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _idempotencyRepo = idempotencyRepo ?? throw new ArgumentNullException(nameof(idempotencyRepo));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor)); // Added
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));         // Added
        }

        /// <summary>
        /// Handles a request to synchronize data with Sage. Creates a TransactionAttempt record.
        /// Signature matches ISyncService. Resolves ApiKeyId internally.
        /// </summary>
        public async Task<SyncInitiatedResponse> RequestSyncAsync(
            FetchSageUpdatesRequest request,
            RequestContext context, // Does NOT contain ApiKeyId/AppId
            CancellationToken ct)
        {
            // Resolve ApiKeyId internally using HttpContext and Repository
            string apiKeyHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Api-Key"].ToString() ?? string.Empty;
            ApiKey? apiKeyEntity = null;
            if (!string.IsNullOrWhiteSpace(apiKeyHeader))
            {
                // Use repo to get the full entity including Id
                apiKeyEntity = await _apiKeyRepository.GetByKeyAsync(apiKeyHeader) ??
                               await _apiKeyRepository.GetByPreviousKeyAsync(apiKeyHeader);
            }

            // Ensure ApiKey was resolved (middleware should have validated, but defense-in-depth)
            if (apiKeyEntity == null)
            {
                _logger.LogError("RequestSyncAsync: Could not resolve ApiKey entity from header value. Header: '{ApiKeyHeader}'. This indicates an issue upstream or misconfiguration.", apiKeyHeader);
                // Throwing ValidationException which controller can map to BadRequest/ProblemDetails
                throw new ValidationException("Invalid or unresolved API Key associated with the request.");
            }

            int apiKeyId = apiKeyEntity.Id; // Extracted ApiKeyId

            // Note: ApiKey model does not contain AppId. AppId scoping relies on ExternalIdLink.
            _logger.LogInformation("RequestSyncAsync received. CorrelationId: {CorrelationId}, ApiKeyId: {ApiKeyId}, EntityType: {EntityType}, ForceFullSync: {ForceFullSync}",
                context.CorrelationId, apiKeyId, request.EntityType, request.ForceFullSync);

            string? idempotencyKeyHash = null;
            Guid jobCorrelationId = Guid.NewGuid(); // This is the NEW CorrelationId for the job/attempt

            // 1. Idempotency Check (if key provided in context)
            if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
            {
                idempotencyKeyHash = HashKeySha512Base64(context.IdempotencyKey);
                _logger.LogDebug("Idempotency key provided. Hash: {IdempotencyKeyHash}", idempotencyKeyHash);

                var existingIdempotencyRecord = await _idempotencyRepo.GetByKeyHashAsync(idempotencyKeyHash, ct);

                if (existingIdempotencyRecord != null)
                {
                    _logger.LogInformation("Idempotency key {IdempotencyKeyHash} already processed.", idempotencyKeyHash);
                    var existingAttempt = await _dbContext.Set<TransactionAttempt>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(ta => ta.IdempotencyKeyHash == idempotencyKeyHash, ct);

                    if (existingAttempt != null)
                    {
                        _logger.LogInformation("Returning existing Job CorrelationId {JobCorrelationId} for idempotency key {IdempotencyKeyHash}.",
                            existingAttempt.CorrelationId, idempotencyKeyHash);
                        // Ensure CorrelationId is parsed correctly
                        if (!Guid.TryParse(existingAttempt.CorrelationId, out var existingGuid))
                        {
                            _logger.LogError("Failed to parse existing CorrelationId '{ExistingCorrelationId}' as Guid for idempotency key {IdempotencyKeyHash}.", existingAttempt.CorrelationId, idempotencyKeyHash);
                            // Fallback or throw? Let's throw for now as this indicates bad data.
                            throw new FormatException($"Invalid CorrelationId format stored for idempotency key {idempotencyKeyHash}.");
                        }
                        return new SyncInitiatedResponse
                        {
                            CorrelationId = existingGuid, // Use the existing job's ID
                            Status = $"Request previously processed. Job CorrelationId: {existingAttempt.CorrelationId}",
                            Timestamp = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Idempotency record found ({IdempotencyKeyHash}) but no matching TransactionAttempt. Returning generic processed response with new Job ID.", idempotencyKeyHash);
                        return new SyncInitiatedResponse
                        {
                            CorrelationId = jobCorrelationId, // Fallback to new ID
                            Status = "Request previously processed (original Job ID lookup failed).",
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
            }

            // 2. Create TransactionAttempts Record
            var transactionAttempt = new TransactionAttempt
            {
                CorrelationId = jobCorrelationId.ToString(),
                ReceivedTimestamp = DateTime.UtcNow,
                SourceSystem = "API",
                TriggeringEventId = context.CorrelationId, // Use incoming HTTP CorrelationId as the trigger ID
                EntityType = request.EntityType,
                ProcessingStatus = "Pending", // Initial status for DB trigger model
                SiteId = context.SiteId,
                CompanyId = context.CompanyId,
                IdempotencyKeyHash = idempotencyKeyHash,
                ApiKeyId = apiKeyId, // Store the resolved ApiKey ID
                AttemptNumber = 1,
                RetryCount = 0,
                // Payload and OriginalHeadersJson can be set here if needed
            };

            try
            {
                _dbContext.Set<TransactionAttempt>().Add(transactionAttempt);
                await _dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("Created TransactionAttempt Id: {AttemptId}, Job CorrelationId: {JobCorrelationId} for ApiKeyId {ApiKeyId}",
                    transactionAttempt.Id, transactionAttempt.CorrelationId, apiKeyId);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Failed to save TransactionAttempt. CorrelationId: {CorrelationId}", context.CorrelationId);
                // Let controller handle mapping to ProblemDetails
                throw;
            }

            // 3. Save Idempotency Record (if key was provided)
            if (idempotencyKeyHash != null)
            {
                try
                {
                    var idemRecord = new IdempotencyRecord
                    {
                        KeyHash = idempotencyKeyHash,
                        CreatedUtc = DateTime.UtcNow
                        // Consider linking to transactionAttempt.Id or jobCorrelationId if needed
                    };
                    await _idempotencyRepo.SaveAsync(idemRecord, ct);
                    _logger.LogDebug("Saved idempotency record for hash {IdempotencyKeyHash}", idempotencyKeyHash);
                }
                catch (Exception idemEx)
                {
                    _logger.LogError(idemEx, "Failed to save idempotency record for hash {IdempotencyKeyHash}. TransactionAttempt {AttemptId} was created.", idempotencyKeyHash, transactionAttempt.Id);
                    // Non-fatal error for idempotency record save
                }
            }

            // 4. Return Response (Background service will pick up the DB record later)
            return new SyncInitiatedResponse
            {
                CorrelationId = jobCorrelationId, // Return the NEW job ID
                Status = "Synchronization request queued successfully.",
                Timestamp = DateTime.UtcNow
            };
        }

        // Helper to hash idempotency key
        private static string HashKeySha512Base64(string key)
        {
            using var sha = SHA512.Create();
            var bytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}

