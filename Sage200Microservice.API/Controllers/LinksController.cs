using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Read-only helper endpoints for external ⇆ Sage ID link resolution.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [Authorize]
    public sealed class LinksController : ControllerBase
    {
        private readonly IExternalIdLinkRepository _links;
        private readonly IApiKeyRepository _apiKeys;

        public LinksController(IExternalIdLinkRepository links, IApiKeyRepository apiKeys)
        {
            _links = links;
            _apiKeys = apiKeys;
        }

        /// <summary>
        /// Resolve a single external reference to its Sage identifier(s).
        /// If appId is omitted, resolves the caller's AppId from the "X-Api-Key" header.
        /// </summary>
        /// <param name="entity">One of: Customer|SopOrder|SalesReceipt|SalesPayment|SalesCreditNote|SalesInvoice</param>
        /// <param name="externalRef">The external reference to resolve.</param>
        /// <param name="appId">Optional explicit AppId; if omitted, inferred from X-Api-Key.</param>
        [HttpGet("resolve")]
        [ProducesResponseType(typeof(LinkResolveResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResolveAsync(
            [FromQuery, Required] string entity,
            [FromQuery, Required] string externalRef,
            [FromQuery] int? appId,
            CancellationToken ct = default)
        {
            // Validate entity type (closed set)
            if (!TryParseEntity(entity, out var entityType))
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Invalid entity",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Allowed: Customer|SopOrder|SalesReceipt|SalesPayment|SalesCreditNote|SalesInvoice"
                });
            }

            if (string.IsNullOrWhiteSpace(externalRef))
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Invalid externalRef",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "The 'externalRef' query parameter is required."
                });
            }

            // Determine AppId (query param wins; else infer from X-Api-Key)
            var effectiveAppId = appId;
            if (effectiveAppId == null)
            {
                var apiKey = Request.Headers["X-Api-Key"].ToString();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/403",
                        Title = "Forbidden",
                        Status = StatusCodes.Status403Forbidden,
                        Detail = "AppId is required via 'appId' or a valid 'X-Api-Key' header."
                    });
                }

                var keyRow = await _apiKeys.GetByKeyAsync(apiKey, ct) ?? await _apiKeys.GetByPreviousKeyAsync(apiKey, ct);
                var valid = await _apiKeys.IsValidKeyAsync(apiKey, ct);
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

                await _apiKeys.UpdateLastUsedAsync(apiKey, ct);
                effectiveAppId = keyRow.Id;
            }

            // Query mapping
            var link = await _links.FindByExternalAsync(effectiveAppId!.Value, entityType, externalRef, ct);
            if (link == null)
            {
                var pd = new ProblemDetails
                {
                    Type = "about:blank",
                    Title = "Mapping not found",
                    Status = StatusCodes.Status404NotFound,
                    Detail = $"No mapping found for appId={effectiveAppId}, entity={entityType}, externalRef='{externalRef}'."
                };
                pd.Extensions["appId"] = effectiveAppId;
                pd.Extensions["entityType"] = entityType.ToString();
                pd.Extensions["externalRef"] = externalRef;
                return NotFound(pd);
            }

            return Ok(new LinkResolveResponseDto
            {
                SageId = link.SageId,
                SageUrn = link.SageUrn,
                SageCode = null // reserved for future use
            });
        }

        /// <summary>
        /// Reverse lookup: list external refs for a given Sage identifier (ID or URN).
        /// </summary>
        /// <param name="entity">One of: Customer|SopOrder|SalesReceipt|SalesPayment|SalesCreditNote|SalesInvoice</param>
        /// <param name="sageId">Numeric Sage identifier, when applicable.</param>
        /// <param name="sageUrn">URN Sage identifier, when applicable.</param>
        /// <param name="page">1-based page index (default 1).</param>
        /// <param name="pageSize">Requested page size; default 50; server cap 100.</param>
        [HttpGet("reverse")]
        [ProducesResponseType(typeof(LinkReverseResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReverseAsync(
            [FromQuery, Required] string entity,
            [FromQuery] long? sageId,
            [FromQuery] string? sageUrn,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            if (!TryParseEntity(entity, out var entityType))
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Invalid entity",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Allowed: Customer|SopOrder|SalesReceipt|SalesPayment|SalesCreditNote|SalesInvoice"
                });
            }

            // Require exactly one of sageId/sageUrn
            var hasId = sageId.HasValue;
            var hasUrn = !string.IsNullOrWhiteSpace(sageUrn);
            if (hasId == hasUrn)
            {
                return BadRequest(new ProblemDetails
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Invalid query",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Provide exactly one of 'sageId' or 'sageUrn'."
                });
            }

            // Normalize paging (cap to 100)
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            var items = hasId
                ? await _links.ListBySageIdAsync(entityType, sageId!.Value, page, pageSize, ct)
                : await _links.ListBySageUrnAsync(entityType, sageUrn!, page, pageSize, ct);

            var dto = new LinkReverseResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Items = items.Select(x => new LinkReverseItemDto
                {
                    AppId = x.AppId,
                    ExternalRef = x.ExternalRef
                }).ToList()
            };

            return Ok(dto);
        }

        // ------------------------ Helpers ------------------------
        private static bool TryParseEntity(string? value, out ExternalEntityType entityType)
        {
            entityType = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Enum.TryParse(value, ignoreCase: true, out entityType);
        }
    }
}
