using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Sage200Microservice.API.Controllers.Infrastructure;
using Sage200Microservice.Services.Interfaces;
using System.Text.Json;

namespace Sage200Microservice.API.Controllers
{
    [ApiController]
    [Route("api/customer-views/export")]
    [Produces("application/json")]
    public sealed class CustomerViewExportController : SageRouteControllerBase
    {
        private readonly ISageApiClient _sage;

        public CustomerViewExportController(ISageApiClient sage, ILogger<CustomerViewExportController> log)
            : base(sage, log)
        {
            _sage = sage;
        }

        [HttpGet]
        public async Task ExportAsync(
            [FromQuery] string format = "csv",
            [FromQuery] int pageSize = 200,
            [FromQuery] int maxPages = 10_000,
            CancellationToken ct = default)
        {
            // Streaming can take a while: give it a longer, but bounded, timeout.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            // Ensure X-Site/X-Company are present for all upstream calls.
            await EnsureRoutingAsync(cts.Token);

            var wantCsv = !string.Equals(format, "ndjson", StringComparison.OrdinalIgnoreCase);
            var ext = wantCsv ? "csv" : "ndjson";
            var file = $"customer_views_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{ext}";

            try
            {
                // 1) Resolve endpoint + paging (RELATIVE paths only; BaseAddress already ends with /accounts/v1/)
                var resolved = await ResolveCustomerViewsPathAsync(pageSize, cts.Token);
                if (resolved is null)
                {
                    var p = new ProblemDetails
                    {
                        Title = "Upstream error while exporting customer views",
                        Detail = "Couldn’t find a Customer Views endpoint on the Sage API.",
                        Status = StatusCodes.Status502BadGateway
                    };
                    Response.StatusCode = p.Status!.Value;
                    Response.ContentType = "application/problem+json";
                    await Response.WriteAsync(JsonSerializer.Serialize(p), cts.Token);
                    return;
                }

                var (rootPath, mode, firstPage) = resolved.Value;

                // 2) Begin streaming
                Response.Headers.ContentDisposition = $"attachment; filename=\"{file}\"";
                Response.Headers.CacheControl = "no-store";
                Response.ContentType = wantCsv ? "text/csv; charset=utf-8" : "application/x-ndjson; charset=utf-8";
                await Response.StartAsync(cts.Token);

                if (wantCsv)
                {
                    await Response.WriteAsync(
                        "Id,Reference,Name,ShortName,OnHold,Status,Balance,CreditLimit," +
                        "Telephone,Email,Website," +
                        "AddrLine1,AddrLine2,AddrLine3,AddrLine4,City,County,Postcode,Country,CountryCode," +
                        "CurrencyCode,DateCreated,DateUpdated\r\n", cts.Token);
                }

                var total = 0;

                // 3) First page
                var wrote = await WriteItemsAsync(firstPage.RootElement, wantCsv, cts.Token);
                total += wrote;

                // 4) Continue paging
                switch (mode)
                {
                    case PagingMode.OData:
                        {
                            var nextPath = NormalizeNext(FindNextLink(firstPage.RootElement));
                            var skip = wrote;
                            var pages = 1;

                            while (!cts.IsCancellationRequested &&
                                   wrote > 0 &&
                                   pages++ < maxPages)
                            {
                                await Task.Delay(200, cts.Token);

                                var path = !string.IsNullOrWhiteSpace(nextPath)
                                    ? nextPath
                                    : $"customer_views?$top={pageSize}&$skip={skip}";

                                using var doc = await _sage.GetAsync<JsonDocument>(path!, cts.Token);
                                wrote = await WriteItemsAsync(doc.RootElement, wantCsv, cts.Token);
                                if (wrote <= 0) break;

                                total += wrote;
                                skip += wrote;
                                nextPath = NormalizeNext(FindNextLink(doc.RootElement));

                                if ((total % 500) == 0) await Response.Body.FlushAsync(cts.Token);
                            }
                            break;
                        }

                    case PagingMode.PageNumber:
                        {
                            var page = 2;
                            while (!cts.IsCancellationRequested &&
                                   wrote == pageSize &&
                                   page <= maxPages)
                            {
                                await Task.Delay(200, cts.Token);

                                var path = QueryHelpers.AddQueryString(rootPath, new Dictionary<string, string?>
                                {
                                    ["pageSize"] = pageSize.ToString(),
                                    ["pageNumber"] = page.ToString()
                                });

                                using var doc = await _sage.GetAsync<JsonDocument>(path, cts.Token);
                                wrote = await WriteItemsAsync(doc.RootElement, wantCsv, cts.Token);
                                if (wrote <= 0) break;

                                total += wrote;
                                page++;

                                if ((total % 500) == 0) await Response.Body.FlushAsync(cts.Token);
                            }
                            break;
                        }

                    case PagingMode.Unpaged:
                        // all written already
                        break;
                }

                if (wantCsv) await Response.WriteAsync($"# rows={total}\r\n", cts.Token);
                await Response.CompleteAsync();
            }
            catch (Exception ex)
            {
                if (!Response.HasStarted)
                {
                    var problem = new ProblemDetails
                    {
                        Title = "Upstream error while exporting customer views",
                        Detail = ex.Message,
                        Status = StatusCodes.Status502BadGateway
                    };
                    Response.StatusCode = problem.Status!.Value;
                    Response.ContentType = "application/problem+json";
                    await Response.WriteAsync(JsonSerializer.Serialize(problem), cts.Token);
                }
                else
                {
                    await Response.WriteAsync($"\r\n# ERROR: export aborted: {ex.Message}\r\n", cts.Token);
                    await Response.CompleteAsync();
                }
            }
        }

        // ---------- detection & paging helpers ----------

        private enum PagingMode { OData, PageNumber, Unpaged }

        /// <summary>
        /// Try OData $top/$skip first, then pageNumber/pageSize, then unpaged.
        /// Use *relative* paths only (BaseUrl already is .../accounts/v1/).
        /// </summary>
        private async Task<(string rootPath, PagingMode mode, JsonDocument firstPage)?> ResolveCustomerViewsPathAsync(
            int pageSize, CancellationToken ct)
        {
            var roots = new[]
            {
                "customer_views",    // spec name
                "customerviews",     // variant seen in some tenants
                "customers/views"    // fallback guess
            };

            foreach (var root in roots)
            {
                // OData
                try
                {
                    var p = $"{root}?$top={pageSize}&$skip=0";
                    var doc = await _sage.GetAsync<JsonDocument>(p, ct);
                    if (HasItems(doc.RootElement)) return (root, PagingMode.OData, doc);
                }
                catch { /* next */ }

                // pageNumber/pageSize
                try
                {
                    var p = QueryHelpers.AddQueryString(root, new Dictionary<string, string?>
                    {
                        ["pageSize"] = pageSize.ToString(),
                        ["pageNumber"] = "1"
                    });
                    var doc = await _sage.GetAsync<JsonDocument>(p, ct);
                    if (HasItems(doc.RootElement)) return (root, PagingMode.PageNumber, doc);
                }
                catch { /* next */ }

                // unpaged
                try
                {
                    var doc = await _sage.GetAsync<JsonDocument>(root, ct);
                    if (HasItems(doc.RootElement)) return (root, PagingMode.Unpaged, doc);
                }
                catch { /* continue */ }
            }

            return null;
        }

        private static bool HasItems(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength() > 0;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("items", out var a) &&
                a.ValueKind == JsonValueKind.Array) return a.GetArrayLength() > 0;
            return false;
        }

        private static bool TryGetItems(JsonElement root, out JsonElement items)
        {
            if (root.ValueKind == JsonValueKind.Array) { items = root; return true; }
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("items", out var arr) &&
                arr.ValueKind == JsonValueKind.Array) { items = arr; return true; }
            items = default;
            return false;
        }

        // ---------- writing ----------

        private async Task<int> WriteItemsAsync(JsonElement root, bool csv, CancellationToken ct)
        {
            if (!TryGetItems(root, out var items)) return 0;

            var count = 0;
            foreach (var c in items.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                string? J(params string[] names) => JAny(c, names);

                // Core
                var id = J("id", "Id");
                var reference = J("reference", "code", "customer_code");
                var name = J("name", "customer_name");
                var shortName = J("short_name", "shortName");
                var onHold = J("account_is_on_hold", "on_hold");
                var status = J("account_status_type");
                var balance = J("balance");
                var creditLimit = J("credit_limit");

                // Contact
                var email = J("contact_default_email_address", "email");
                var phone = J("contact_default_telephone_number", "telephone");
                if (string.IsNullOrWhiteSpace(phone))
                {
                    var cc = J("telephone_country_code");
                    var ac = J("telephone_area_code");
                    var sub = J("telephone_subscriber_number");
                    var parts = new[] { cc, ac, sub }.Where(s => !string.IsNullOrWhiteSpace(s));
                    phone = parts.Any() ? string.Join(" ", parts) : null;
                }

                var website = J("website");

                // Address (from customer_location_* fields)
                var a1 = J("customer_location_address_line_1");
                var a2 = J("customer_location_address_line_2");
                var a3 = J("customer_location_address_line_3");
                var a4 = J("customer_location_address_line_4");
                var city = J("customer_location_city");
                var county = J("customer_location_county");
                var post = J("customer_location_post_code");
                var country = J("customer_location_country");
                var ctryCd = J("customer_location_country_code", "country_code");

                // Currency code (short)
                var currIso = J("currency_iso_code", "currency_name");

                // Timestamps
                var created = J("date_time_created");
                var updated = J("date_time_updated");

                if (csv)
                {
                    await Response.WriteAsync(
                        $"{Csv(id)},{Csv(reference)},{Csv(name)},{Csv(shortName)},{Csv(onHold)},{Csv(status)}," +
                        $"{Csv(balance)},{Csv(creditLimit)}," +
                        $"{Csv(phone)},{Csv(email)},{Csv(website)}," +
                        $"{Csv(a1)},{Csv(a2)},{Csv(a3)},{Csv(a4)},{Csv(city)},{Csv(county)},{Csv(post)},{Csv(country)},{Csv(ctryCd)}," +
                        $"{Csv(currIso)},{Csv(created)},{Csv(updated)}\r\n", ct);
                }
                else
                {
                    await Response.WriteAsync(c.GetRawText() + "\n", ct); // NDJSON
                }

                count++;
            }
            return count;
        }

        // ---------- link & JSON helpers ----------

        private static string? FindNextLink(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("@odata.nextLink", out var odata) && odata.ValueKind == JsonValueKind.String)
                return odata.GetString();

            if (root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String)
                return next.GetString();

            if (root.TryGetProperty("links", out var links) &&
                links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("next", out var linkNext) &&
                linkNext.ValueKind == JsonValueKind.String)
                return linkNext.GetString();

            if (root.TryGetProperty("page", out var pageObj) &&
                pageObj.ValueKind == JsonValueKind.Object &&
                pageObj.TryGetProperty("next", out var pageNext) &&
                pageNext.ValueKind == JsonValueKind.String)
                return pageNext.GetString();

            return null;
        }

        /// <summary>Make absolute/rooted next links relative to HttpClient.BaseAddress.</summary>
        private static string? NormalizeNext(string? next)
        {
            if (string.IsNullOrWhiteSpace(next)) return null;
            if (Uri.TryCreate(next, UriKind.Absolute, out var abs))
                next = abs.PathAndQuery;
            return next.TrimStart('/');
        }

        private static string Csv(string? s)
            => string.IsNullOrEmpty(s) ? "" :
               (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"");

        private static string? JAny(JsonElement obj, params string[] names)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var v) &&
                    (v.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                    return v.ToString();
            return null;
        }
    }
}
