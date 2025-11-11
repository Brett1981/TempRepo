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
        public async Task<IActionResult> GetAsync([FromQuery] string endpoint = "departments", CancellationToken ct = default)
        {
            if (!_env.IsDevelopment()) return Forbid();
            if (string.IsNullOrWhiteSpace(endpoint)) return BadRequest("Query 'endpoint' required.");

            // Hit Sage and return status + body for diagnosis
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var resp = await _api.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            return StatusCode((int)resp.StatusCode, new
            {
                status = (int)resp.StatusCode,
                body = text
            });
        }

        [HttpGet("sites")]
        [Produces("application/json")]
        public async Task<IActionResult> GetSitesAsync(CancellationToken ct)
        {
            if (!_env.IsDevelopment()) return Forbid();

            using var req = new HttpRequestMessage(HttpMethod.Get, "/uk/sage200extra/accounts/v1/sites");
            using var resp = await _api.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            return StatusCode((int)resp.StatusCode, new { status = (int)resp.StatusCode, body = text });
        }

        [HttpGet("departments-via-sites")]
        [Produces("application/json")]
        public async Task<IActionResult> GetDepartmentsViaSitesAsync(CancellationToken ct)
        {
            if (!_env.IsDevelopment()) return Forbid();

            // 1) Fetch sites for the signed-in Sage ID user
            using var sitesReq = new HttpRequestMessage(HttpMethod.Get, "/uk/sage200extra/accounts/v1/sites");
            using var sitesResp = await _api.SendAsync(sitesReq, ct);
            var sitesJson = await sitesResp.Content.ReadAsStringAsync(ct);
            if (!sitesResp.IsSuccessStatusCode)
                return StatusCode((int)sitesResp.StatusCode, new { status = (int)sitesResp.StatusCode, body = sitesJson });

            // 2) Pick a site/company the user has (simplest: first)
            //    The “sites” payload contains siteId (GUID) and companies with numeric companyId
            using var doc = JsonDocument.Parse(sitesJson);
            var firstSite = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (firstSite.ValueKind != JsonValueKind.Object)
                return BadRequest("No sites returned for this user/token.");

            var siteId = firstSite.GetProperty("siteId").GetString();
            var firstCompany = firstSite.GetProperty("companies").EnumerateArray().FirstOrDefault();
            var companyId = firstCompany.ValueKind == JsonValueKind.Object
                ? firstCompany.GetProperty("companyId").GetInt32().ToString()
                : null;

            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(companyId))
                return BadRequest("No company found for this site.");

            // 3) Call departments with explicit headers (overrides any defaults)
            using var deptReq = new HttpRequestMessage(HttpMethod.Get, "/uk/sage200extra/accounts/v1/departments");
            deptReq.Headers.Remove("X-Site"); deptReq.Headers.TryAddWithoutValidation("X-Site", siteId);
            deptReq.Headers.Remove("X-Company"); deptReq.Headers.TryAddWithoutValidation("X-Company", companyId);

            using var deptResp = await _api.SendAsync(deptReq, ct);
            var deptText = await deptResp.Content.ReadAsStringAsync(ct);
            return StatusCode((int)deptResp.StatusCode, new { status = (int)deptResp.StatusCode, body = deptText, siteId, companyId });
        }
    }
}
