// SalesCreditNotesController.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sage200Microservice.API.Controllers.Infrastructure;   // SageRouteControllerBase
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sales;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class SalesCreditNotesController : SageRouteControllerBase
    {
        private readonly ISalesCreditNotesService _svc;
        private readonly ILogger<SalesCreditNotesController> _log;
        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;

        public SalesCreditNotesController(
            ISalesCreditNotesService svc,
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys,
            ISageApiClient sage,                                  // needed by SageRouteControllerBase
            ILogger<SalesCreditNotesController> log)
            : base(sage, log)
        {
            _svc = svc;
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
            _log = log;
        }

        /// <summary>
        /// POST /api/SalesCreditNotes — creates a Sales Credit Note in Sage (returns URN on success).
        /// </summary>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(SalesCreateResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(SalesCreateResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> CreateAsync([FromBody] SalesCreditNoteCreate request, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new SalesCreateResult
                {
                    Success = false,
                    Message = "Validation failed.",
                    Failure = FailureKind.BadRequest
                });
            }

            // Ensure routing headers (X-Site / X-Company) are present or discoverable.
            await EnsureRoutingAsync(ct);

            // Guarantee an Idempotency-Key: use caller's if supplied; otherwise generate a stable hash.
            if (!Request.Headers.ContainsKey("Idempotency-Key"))
            {
                Request.Headers["Idempotency-Key"] = GenerateIdempotencyKey(request);
            }

            // Perform creation via service (service will read HttpContext headers incl. idempotency).
            SalesCreateResult result;
            try
            {
                result = await _svc.CreateAsync(request, HttpContext, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                var pd = new ProblemDetails
                {
                    Type = "https://httpstatuses.com/502",
                    Title = "Upstream Sage error",
                    Status = StatusCodes.Status502BadGateway,
                    Detail = ex.Message
                };
                return StatusCode(StatusCodes.Status502BadGateway, pd);
            }

            // Map typed failures
            if (result.Failure == FailureKind.BadRequest)
                return BadRequest(result);

            if (result.Failure == FailureKind.Upstream)
            {
                var pd = new ProblemDetails
                {
                    Type = "https://httpstatuses.com/502",
                    Title = "Upstream Sage error",
                    Status = StatusCodes.Status502BadGateway,
                    Detail = string.IsNullOrWhiteSpace(result.UpstreamBody) ? (result.Message ?? "Upstream error from Sage.") : result.UpstreamBody
                };
                return StatusCode(StatusCodes.Status502BadGateway, pd);
            }

            // On success, persist an ExternalIdLink if we can resolve AppId.
            // Try: request.ExternalRefs[].AppId, else resolve from X-Api-Key.
            int? appId = request.ExternalRefs?.FirstOrDefault()?.AppId;
            if (appId is null)
            {
                var apiKey = Request.Headers["X-Api-Key"].ToString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
                    var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
                    if (keyRow != null && valid)
                    {
                        await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
                        appId = keyRow.Id;
                    }
                }
            }

            // Best-effort link insert (no-op if we can't determine an AppId).
            if (appId is not null)
            {
                try
                {
                    await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        await _links.TryInsertAsync(new ExternalIdLink
                        {
                            AppId = appId.Value,
                            EntityType = ExternalEntityType.SalesCreditNote,
                            SageId = null,
                            SageUrn = result.Urn?.ToString(),
                            ExternalRef =
                                request.ExternalRefs?.FirstOrDefault()?.ExternalRef
                                ?? request.SecondReference
                                ?? request.Reference
                                ?? string.Empty,
                            CreatedUtc = DateTime.UtcNow
                        }, ct);
                    });
                }
                catch (InvalidOperationException ex)
                {
                    _log.LogWarning(ex, "ExternalIdLink conflict for SalesCreditNote {Urn}", result.Urn?.ToString());
                    // Do not fail the API on link conflict; the credit note was created upstream.
                }
            }

            return Ok(result);
        }

        // ---------- helpers ----------

        // Stable, null-stripped JSON hash for idempotency when callers do not supply a key.
        private static string GenerateIdempotencyKey(SalesCreditNoteCreate body)
        {
            // serialize with nulls omitted for stability
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash); // uppercase hex
        }
    }
}
