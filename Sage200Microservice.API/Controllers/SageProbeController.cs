using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.API.Helpers;
using Sage200Microservice.Services.Interfaces;
using System.Text.Json;
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
#if DEBUG
        [AllowAnonymous] // dev-only probe
#endif
        public async Task<IActionResult> GetAsync([FromQuery] string endpoint, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return BadRequest("endpoint query is required, e.g. 'customers?pagesize=5'");

            // 1) Resolve sites for this token (exactly like your working departments-via-sites)
            var sitesJson = await _api.GetAsync<string>("sites", ct); // typed client baseUrl already set
            using var doc = JsonDocument.Parse(sitesJson);
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return Unauthorized(new { status = 401, body = "", reason = "No sites for token" });

            // NOTE: the /sites payload uses snake_case (site_id / company_id) per the data you logged.
            var siteId = first.TryGetProperty("site_id", out var s) ? s.GetString() : null;
            var companyId = first.TryGetProperty("company_id", out var c) ? c.GetRawText() : null;
            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(companyId))
                return Unauthorized(new { status = 401, body = "", reason = "Token has no site/company" });

            // 2) Stash for SageRoutingHeaderHandler
            Helper.SetRouting(HttpContext, siteId!, companyId!);

            // 3) Call through
            try
            {
                var body = await _api.GetAsync<string>(endpoint, ct);
                return Ok(new { status = 200, body });
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Pass through minimal 401 for the probe (to match your current response)
                return StatusCode(401, new { status = 401, body = "" });
            }
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
        public async Task<IActionResult> GetDepartmentsViaSitesAsync(
     [FromQuery] string? site = null,
     [FromQuery] string? company = null,
     CancellationToken ct = default)
        {
            if (!_env.IsDevelopment()) return Forbid();

            // If caller already supplied site/company, just use them
            if (!string.IsNullOrWhiteSpace(site) && !string.IsNullOrWhiteSpace(company))
                return await CallDepartmentsAsync(site!, company!, ct);

            // 1) Fetch /sites
            using var sitesReq = new HttpRequestMessage(HttpMethod.Get, "/uk/sage200extra/accounts/v1/sites");
            using var sitesResp = await _api.SendAsync(sitesReq, ct);
            var sitesJson = await sitesResp.Content.ReadAsStringAsync(ct);

            if (!sitesResp.IsSuccessStatusCode)
                return StatusCode((int)sitesResp.StatusCode, new { status = (int)sitesResp.StatusCode, body = sitesJson });

            // 2) Resolve site/company from payload (snake_case aware)
            if (!Helper.TryResolveFirstSiteCompany_SnakeCase(sitesJson, out var siteId, out var companyId, out var diag))
                return BadRequest(new
                {
                    error = "Could not resolve site/company from /sites payload.",
                    diagnostics = diag,
                    preview = sitesJson.Length > 1200 ? sitesJson[..1200] + "…" : sitesJson
                });

            // 3) Call departments with explicit headers
            return await CallDepartmentsAsync(siteId!, companyId!, ct);

            async Task<IActionResult> CallDepartmentsAsync(string s, string c, CancellationToken token)
            {
                using var deptReq = new HttpRequestMessage(HttpMethod.Get, "/uk/sage200extra/accounts/v1/departments");
                deptReq.Headers.Remove("X-Site"); deptReq.Headers.TryAddWithoutValidation("X-Site", s);
                deptReq.Headers.Remove("X-Company"); deptReq.Headers.TryAddWithoutValidation("X-Company", c);

                using var deptResp = await _api.SendAsync(deptReq, token);
                var deptText = await deptResp.Content.ReadAsStringAsync(token);
                return StatusCode((int)deptResp.StatusCode, new { status = (int)deptResp.StatusCode, siteId = s, companyId = c, body = deptText });
            }
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    }
}
