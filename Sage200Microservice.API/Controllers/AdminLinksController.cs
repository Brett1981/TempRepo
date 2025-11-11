using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.API.Middleware;
using Sage200Microservice.Data;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Administrative (optional) endpoint to backfill/repair cross-application → Sage mappings.
    /// </summary>
    [ApiController]
    [Route("api/admin/links")]
    [SkipAudit]
    [Produces("application/json")]
    public sealed class AdminLinksController : ControllerBase
    {
        private const int MaxBatch = 1000;

        private readonly ApplicationContext _db;
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;

        public AdminLinksController(
            ApplicationContext db,
            IExternalIdLinkRepository links,
            IApiKeyRepository apiKeys)
        {
            _db = db;
            _links = links;
            _apiKeys = apiKeys;
        }

        /// <summary>
        /// Backfill (or dry-run) ExternalIdLink entries from explicit items.
        /// </summary>
        /// <param name="dryRun">When true (default), validate and report without inserting.</param>
        [HttpPost("backfill")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(BackfillResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> BackfillAsync(
            [FromBody] BackfillRequestDto request,
            [FromQuery] bool dryRun = true,
            CancellationToken ct = default)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "No items",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Provide at least one item in the request body."
                });
            }

            if (request.Items.Count > MaxBatch)
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Batch too large",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"A maximum of {MaxBatch} items is allowed per call."
                });
            }

            // If many items omit appId, caller must provide a valid X-Api-Key once.
            int? headerAppId = null;
            var anyMissingAppId = request.Items.Any(i => i.AppId is null);
            if (anyMissingAppId)
            {
                var apiKey = Request.Headers["X-Api-Key"].ToString();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/403",
                        Title = "Forbidden",
                        Status = StatusCodes.Status403Forbidden,
                        Detail = "AppId is required on each item or resolvable once from 'X-Api-Key'."
                    });
                }

                var keyRow = await _apiKeys.GetByKeyAsync(apiKey) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey);
                var valid = await _apiKeys.IsValidKeyAsync(apiKey);
                if (keyRow == null || !valid)
                {
                    return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/401",
                        Title = "Unauthorized",
                        Status = StatusCodes.Status401Unauthorized,
                        Detail = "API key could not be resolved to a valid AppId."
                    });
                }
                await _apiKeys.UpdateLastUsedAsync(apiKey);
                headerAppId = keyRow.Id;
            }

            var resp = new BackfillResponseDto { DryRun = dryRun, Total = request.Items.Count };
            var attempted = 0;
            var inserted = 0;
            var conflicts = 0;
            var invalid = 0;

            foreach (var item in request.Items)
            {
                ct.ThrowIfCancellationRequested();

                var result = new BackfillResultItemDto
                {
                    Entity = item.Entity,
                    ExternalRef = item.ExternalRef ?? string.Empty
                };

                // Validate entity (closed set)
                if (!TryParseEntity(item.Entity, out var entityType))
                {
                    result.Outcome = "invalid";
                    result.Reason = $"Unsupported entity '{item.Entity}'. Allowed: Customer|SopOrder|SalesReceipt|SalesPayment|SalesCreditNote|SalesInvoice.";
                    resp.Items.Add(result);
                    invalid++;
                    continue;
                }

                // Determine AppId for this item
                var effectiveAppId = item.AppId ?? headerAppId;
                if (effectiveAppId is null)
                {
                    result.Outcome = "invalid";
                    result.Reason = "AppId is required via item.appId or resolvable from 'X-Api-Key'.";
                    resp.Items.Add(result);
                    invalid++;
                    continue;
                }
                result.AppId = effectiveAppId.Value;

                // Canonical identifier rule per entity
                var requiresId = entityType == ExternalEntityType.Customer || entityType == ExternalEntityType.SopOrder;
                var requiresUrn = !requiresId; // receipts/payments/credit notes/invoices

                if (requiresId && item.SageId is null)
                {
                    result.Outcome = "invalid";
                    result.Reason = $"Entity '{entityType}' requires numeric SageId.";
                    resp.Items.Add(result);
                    invalid++;
                    continue;
                }
                if (requiresUrn && string.IsNullOrWhiteSpace(item.SageUrn))
                {
                    result.Outcome = "invalid";
                    result.Reason = $"Entity '{entityType}' requires SageUrn.";
                    resp.Items.Add(result);
                    invalid++;
                    continue;
                }

                result.SageId = item.SageId;
                result.SageUrn = item.SageUrn;

                // When dry run → just state "wouldInsert" / "exists" / "conflict?" best-effort via unique key lookup
                if (dryRun)
                {
                    var existing = await _links.FindByExternalAsync(effectiveAppId.Value, entityType, item.ExternalRef, ct);
                    if (existing == null)
                    {
                        result.Outcome = "inserted"; // would insert
                        result.Reason = "Dry-run: would insert.";
                    }
                    else
                    {
                        var sameId = existing.SageId == item.SageId;
                        var sameUrn = string.Equals(existing.SageUrn, item.SageUrn, StringComparison.Ordinal);
                        if (sameId && sameUrn)
                        {
                            result.Outcome = "exists";
                            result.Reason = "Mapping already exists (idempotent).";
                        }
                        else
                        {
                            result.Outcome = "conflict";
                            result.Reason = $"Existing differs (existing: id={existing.SageId}, urn={existing.SageUrn}; request: id={item.SageId}, urn={item.SageUrn}).";
                            conflicts++;
                        }
                    }

                    resp.Items.Add(result);
                    attempted++;
                    continue;
                }

                // Live insert (idempotent via repository)
                try
                {
                    var insertedNow = await _links.TryInsertAsync(new ExternalIdLink
                    {
                        AppId = effectiveAppId.Value,
                        EntityType = entityType,
                        SageId = item.SageId,
                        SageUrn = item.SageUrn,
                        ExternalRef = item.ExternalRef
                    }, ct);

                    result.Outcome = insertedNow ? "inserted" : "exists";
                    if (!insertedNow) result.Reason = "Mapping already exists (idempotent).";
                    resp.Items.Add(result);

                    attempted++;
                    if (insertedNow) inserted++;
                }
                catch (InvalidOperationException ex)
                {
                    result.Outcome = "conflict";
                    result.Reason = ex.Message;
                    resp.Items.Add(result);

                    attempted++;
                    conflicts++;
                }
            }

            resp.Attempted = attempted;
            resp.Inserted = inserted;
            resp.Conflicts = conflicts;
            resp.Invalid = invalid;

            return Ok(resp);
        }

        // ------------------------
        // Helpers
        // ------------------------
        private static bool TryParseEntity(string? value, out ExternalEntityType entityType)
        {
            entityType = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return Enum.TryParse<ExternalEntityType>(value, ignoreCase: true, out entityType);
        }
    }
}