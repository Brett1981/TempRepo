using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.Attributes;            // SageRoutingHeaders
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// REST endpoint for amending SOP Order document status (SOP-Orders-Status).
    /// Route: POST /api/sop/orders/{id}/status
    /// </summary>
    [ApiController]
    [Route("api/sop/orders/{id:long}/status")]
    [Authorize(Policy = "ApiUser")]
    [Produces("application/json")]
    public sealed class SopOrderStatusController : ControllerBase
    {
        private readonly ISopOrderStatusService _service;
        private readonly IValidator<SopOrderStatusUpdate> _validator;
        private readonly ILogger<SopOrderStatusController> _log;

        public SopOrderStatusController(
            ISopOrderStatusService service,
            IValidator<SopOrderStatusUpdate> validator,
            ILogger<SopOrderStatusController> log)
        {
            _service = service;
            _validator = validator;
            _log = log;
        }

        /// <summary>
        /// Updates a SOP order's document status to one of (Live, OnHold, Cancelled, Completed).
        /// </summary>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(SopOrderStatusUpdateResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        // Advertise/require routing headers like other endpoints (no idempotency needed here).
        [SageRoutingHeaders(DocumentApiKey = true)]
        public async Task<IActionResult> UpdateAsync(long id, [FromBody] SopOrderStatusUpdate body, CancellationToken ct)
        {
            body ??= new SopOrderStatusUpdate();
            body.OrderId = id;

            var vr = await _validator.ValidateAsync(body, ct);
            if (!vr.IsValid)
            {
                foreach (var e in vr.Errors)
                    ModelState.AddModelError(e.PropertyName, e.ErrorMessage);

                return ValidationProblem(ModelState);
            }

            // Service reads required headers (X-Site/X-Company/etc.) from HttpContext as per your pattern.
            var result = await _service.UpdateStatusAsync(body, HttpContext, ct);

            // If your result type includes failure semantics you want to surface as ProblemDetails,
            // you can branch here similar to SalesInvoicesController. For now we mirror your simple success shape.
            return Ok(result);
        }
    }
}
