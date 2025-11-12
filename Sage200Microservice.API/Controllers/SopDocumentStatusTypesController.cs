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
    [ApiExplorerSettings(IgnoreApi = true)] // Hide controller from Swagger to avoid conflict with gRPC JSON-transcoding
    public sealed class SopDocumentStatusTypesController : ControllerBase
    {
        private readonly ISopDocumentStatusTypeService _service;

        public SopDocumentStatusTypesController(ISopDocumentStatusTypeService service)
        {
            _service = service;
        }

        /// <summary>
        /// Returns the list of SOP document status types (code/name/description).
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<SopDocumentStatusTypeDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(CancellationToken ct)
        {
            var items = await _service.ListAsync(HttpContext, ct);
            return Ok(items);
        }
    }


}