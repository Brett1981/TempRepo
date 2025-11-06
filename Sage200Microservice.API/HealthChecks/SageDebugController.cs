using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.Services.Interfaces;
using System.Text.Json;

namespace Sage200Microservice.API.HealthChecks
{
    [ApiController, Route("api/debug/sage")]
    public class SageDebugController : ControllerBase
    {
        private readonly ISageApiClient _sage;
        public SageDebugController(ISageApiClient s) => _sage = s;

        [HttpGet("ping")]
        public async Task<IActionResult> Ping([FromQuery] string path = "customer_views?$top=1")
        {
            try
            {
                using var doc = await _sage.GetAsync<JsonDocument>(path, HttpContext.RequestAborted);
                return Content(doc.RootElement.GetRawText(), "application/json");
            }
            catch (Exception ex)
            {
                return Problem(title: "Upstream call failed", detail: ex.Message, statusCode: 502);
            }
        }
    }
}
