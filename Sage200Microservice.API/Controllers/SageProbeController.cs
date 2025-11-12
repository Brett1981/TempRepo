using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/sage-probe")]
    public class SageProbeController : ControllerBase
    {
        private readonly ISageApiClient _api;
        private readonly IHostEnvironment _env;

        public SageProbeController(ISageApiClient api, IHostEnvironment env)
        {
            _api = api;
            _env = env;
        }

        /// <summary>
        /// DEV ONLY: GET passthrough. Example: /api/sage-probe/get?endpoint=customers?pagesize=5
        /// </summary>
        [HttpGet("get")]
        [Produces("application/json")]
        public async Task<IActionResult> GetAsync([FromQuery] string endpoint, CancellationToken ct)
        {
            if (!_env.IsDevelopment()) return Forbid();
            if (string.IsNullOrWhiteSpace(endpoint)) return BadRequest("Query 'endpoint' required.");
            var json = await _api.GetAsync<JsonElement>(endpoint, ct);
            return Ok(json);
        }
    }
}
