using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.API.Models;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Views;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/customer-views/sop-invoice-credit-lines")]
[Produces("application/json")]
public sealed class CustomerSopInvoiceCreditLineViewController : ControllerBase
{
    private readonly ILogger<CustomerSopInvoiceCreditLineViewController> _log;
    private readonly ISageApiClient _sage;

    public CustomerSopInvoiceCreditLineViewController(
        ILogger<CustomerSopInvoiceCreditLineViewController> log,
        ISageApiClient sage)
    { _log = log; _sage = sage; }

    /// <summary>
    /// Returns SOP invoice credit line views. Supports $filter, $orderby, $select, $top, $skip, $search.
    /// Defaults: $orderby = sop_invoice_credit_id desc, sop_invoice_credit_line_id desc; $top = 100 (cap 1000).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ViewListResult<CustomerSopInvoiceCreditLineViewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromQuery(Name = "$filter")] string? filter,
        [FromQuery(Name = "$orderby")] string? orderby,
        [FromQuery(Name = "$select")] string? select,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery(Name = "$search")] string? search,
        CancellationToken ct)
    {

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Optional pass-through if caller supplied them; otherwise SageAuthDelegatingHandler injects defaults.
        if (Request.Headers.TryGetValue("X-Site", out var xSite) && !StringValues.IsNullOrEmpty(xSite))
            headers["X-Site"] = xSite.ToString();
        if (Request.Headers.TryGetValue("X-Company", out var xCompany) && !StringValues.IsNullOrEmpty(xCompany))
            headers["X-Company"] = xCompany.ToString();

        var path = "customer_sop_invoice_credit_line_views";
        var qs = BuildQueryStringWithDefaults(Request.Query, filter, orderby, select, top, skip, search);
        var url = string.IsNullOrEmpty(qs) ? path : $"{path}?{qs}";

        var (status, body) = await _sage.GetForBodyAsync(url, headers, ct);
        if (status is < 200 or > 299)
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails { Title = "Upstream error", Detail = SafePreview(body) });

        var items = new List<CustomerSopInvoiceCreditLineViewDto>(); string? next = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                items = JsonSerializer.Deserialize<List<CustomerSopInvoiceCreditLineViewDto>>(root.GetRawText()) ?? new();
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    items = JsonSerializer.Deserialize<List<CustomerSopInvoiceCreditLineViewDto>>(arr.GetRawText()) ?? new();
                if (root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String) next = n.GetString();
                else if (root.TryGetProperty("next_page", out var np) && np.ValueKind == JsonValueKind.String) next = np.GetString();
                else if (root.TryGetProperty("@odata.nextLink", out var ol) && ol.ValueKind == JsonValueKind.String) next = ol.GetString();
            }
        }
        catch { return Ok(new ViewListResult<CustomerSopInvoiceCreditLineViewDto> { Items = [], Raw = body }); }

        return Ok(new ViewListResult<CustomerSopInvoiceCreditLineViewDto> { Items = items, Next = next });
    }

    /// <summary>
    /// Builds the upstream query string for the SOP invoice credit line view,
    /// merging any existing query params with optional overrides and applying sensible defaults.
    ///
    /// Behavior:
    /// - PRESERVE OData keys (those starting with '$') literally (do NOT URL-encode the key).
    /// - Always URL-encode VALUES.
    /// - Default $orderby to "sop_invoice_credit_id desc, sop_invoice_credit_line_id desc" when missing/blank.
    /// - Default $top to 100 when missing/invalid; cap $top at 1000.
    /// - Optional normalization: if $filter contains quoted numerics for known Int64 fields,
    ///   e.g. customer_id eq '123', convert to customer_id eq 123 (helps avoid Edm.Int64 vs Edm.String errors).
    /// </summary>
    private static string BuildQueryStringWithDefaults(
        IQueryCollection original, string? filter, string? orderby, string? select, int? top, int? skip, string? search)
    {
        // Merge original query into a case-insensitive multi-value map
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in original)
            dict[kv.Key] = kv.Value.ToList();

        // Normalize any incoming $filter in the original query (quoted numerics -> numerics)
        if (dict.TryGetValue("$filter", out var origFilters) && origFilters is { Count: > 0 })
        {
            dict["$filter"] = origFilters
                .Select(NormalizeFilterNumbers)
                .ToList();
        }

        // Apply explicit overrides (single-valued)
        void Put(string k, string? v) { if (v != null) dict[k] = new() { v }; }
        void PutI(string k, int? v) { if (v != null) dict[k] = new() { v.Value.ToString() }; }

        Put("$filter", filter is null ? null : NormalizeFilterNumbers(filter));
        Put("$orderby", orderby);
        Put("$select", select);
        Put("$search", search);
        PutI("$top", top);
        PutI("$skip", skip);

        // Ensure default $orderby when missing/blank
        if (!dict.TryGetValue("$orderby", out var obVals) || string.IsNullOrWhiteSpace(obVals?.FirstOrDefault()))
        {
            dict["$orderby"] = new()
        {
            "sop_invoice_credit_id desc, sop_invoice_credit_line_id desc"
        };
        }

        // Ensure default/capped $top
        var topValue = 100; // default
        if (dict.TryGetValue("$top", out var topVals) && int.TryParse(topVals.FirstOrDefault(), out var parsedTop))
        {
            topValue = parsedTop;
        }
        if (topValue <= 0) topValue = 100;
        if (topValue > 1000) topValue = 1000;
        dict["$top"] = new() { topValue.ToString() };

        // Build query string:
        //  - Keys that start with '$' are written literally (not URL-encoded).
        //  - All values are URL-encoded.
        var sb = new StringBuilder();
        foreach (var (k, vals) in dict)
        {
            if (vals is null || vals.Count == 0) continue;

            var keyOut = k.StartsWith("$", StringComparison.Ordinal) ? k : Uri.EscapeDataString(k);

            foreach (var v in vals)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(keyOut);
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(v ?? string.Empty));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Best-effort normalization for OData $filter, converting quoted integer literals for known Int64 fields
    /// into unquoted numerics (e.g., <c>customer_id eq '123'</c> → <c>customer_id eq 123</c>).
    /// This prevents the upstream from rejecting with "Edm.Int64 vs Edm.String" errors.
    /// </summary>
    private static string NormalizeFilterNumbers(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return filter;

        // Known Int64 fields in this view (extend if needed)
        // customer_id, sop_invoice_credit_id, sop_invoice_credit_line_id,
        // invoice_line_profit_analysis_id, product_id, product_group_id
        // Pattern: <field> <op> '123'  ->  <field> <op> 123
        // Handles eq, ne, gt, ge, lt, le
        var longIdFields = new[]
        {
        "customer_id",
        "sop_invoice_credit_id",
        "sop_invoice_credit_line_id",
        "invoice_line_profit_analysis_id",
        "product_id",
        "product_group_id"
    };

        var ops = @"eq|ne|gt|ge|lt|le";
        foreach (var f in longIdFields)
        {
            // Replace occurrences like:  customer_id eq '123'  →  customer_id eq 123
            // Regex is case-insensitive on operator, but field is matched case-sensitively as sent.
            var pattern = $@"\b{f}\s+(?:{ops})\s+'(\d+)'\b";
            filter = System.Text.RegularExpressions.Regex.Replace(
                filter,
                pattern,
                m => $"{f} {m.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]} {m.Groups[1].Value}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return filter;
    }


    private static string SafePreview(string? s, int max = 512) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
}
