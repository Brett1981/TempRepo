using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Exposes the SOP Document Status Types from Sage 200.
    /// Route: GET /api/sop/document-status-types
    /// </summary>
    [ApiController]
    [Route("api/sop/document-status-types")]
    [Authorize(Policy = "ApiUser")]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)] // Hide from Swagger if you use gRPC JSON-transcoding
    public sealed class SopDocumentStatusTypesController : ControllerBase
    {
        private readonly ISopDocumentStatusTypeService _service;

        public SopDocumentStatusTypesController(ISopDocumentStatusTypeService service)
        {
            _service = service;
        }

        /// <summary>Returns the list of SOP document status types (code/name/description).</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<SopDocumentStatusTypeDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetAsync(CancellationToken ct)
        {
            try
            {
                // If your service inspects headers for routing (X-Site / X-Company), passing HttpContext is enough.
                var items = await _service.ListAsync(HttpContext, ct);
                return Ok(items);
            }
            catch (HttpRequestException ex)
            {
                // Upstream/Sage error → 502 to the caller with a brief detail
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Type = "https://httpstatuses.com/502",
                    Title = "Upstream error",
                    Status = StatusCodes.Status502BadGateway,
                    Detail = ex.Message
                });
            }
        }
    }
}
