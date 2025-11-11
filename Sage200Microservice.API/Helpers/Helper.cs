using System.Text.Json;

namespace Sage200Microservice.API.Helpers
{
    public static class Helper
    {

        public static void SetRouting(HttpContext ctx, string siteId, string companyId)
        {
            ctx.Items["X-Site"] = siteId;
            ctx.Items["X-Company"] = companyId;
        }

        public static bool TryResolveFirstSiteCompany(
    string json,
    out string siteId,
    out string companyId,
    out object diagnostics)
        {
            siteId = string.Empty;
            companyId = string.Empty;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Find the array that actually contains sites. Common possibilities:
            // - payload IS an array
            // - payload has an array property like "sites", "items", "value", "data"
            if (!TryGetSitesArray(root, out var sitesArr, out var containerInfo))
            {
                diagnostics = new
                {
                    message = "No array of sites located.",
                    containerInfo
                };
                return false;
            }

            // Pick the first site-like object
            foreach (var siteObj in sitesArr.EnumerateArray())
            {
                if (siteObj.ValueKind != JsonValueKind.Object) continue;

                // Try common names (case-insensitive), then a generic "id" fallback
                if (!TryGetStringCI(siteObj, "siteId", out siteId) &&
                    !TryGetStringCI(siteObj, "siteID", out siteId) &&
                    !TryGetStringCI(siteObj, "siteGuid", out siteId) &&
                    !TryGetStringCI(siteObj, "id", out siteId))
                {
                    // Keep scanning other elements
                    continue;
                }

                // Companies could be an array property "companies" / "companyList" / etc.
                if (!TryGetFirstCompanyId(siteObj, out companyId, out var compDiag))
                {
                    // If we found siteId but no company, keep looking at other sites
                    // but keep last diagnostics
                    diagnostics = new
                    {
                        message = "Found siteId but no companyId.",
                        siteObjectKeys = siteObj.EnumerateObject().Select(p => p.Name).ToArray(),
                        compDiag
                    };
                    siteId = string.Empty;
                    continue;
                }

                diagnostics = new
                {
                    containerInfo,
                    siteObjectKeys = siteObj.EnumerateObject().Select(p => p.Name).ToArray()
                };
                return true;
            }

            diagnostics = new
            {
                message = "Scanned array, but no site object with siteId/companyId was found.",
                containerInfo,
                firstElementPreview = sitesArr.ValueKind == JsonValueKind.Array && sitesArr.GetArrayLength() > 0
                    ? sitesArr[0].ToString()
                    : "(empty)"
            };
            return false;
        }

        public static bool TryResolveFirstSiteCompany_SnakeCase(
    string json,
    out string? siteId,
    out string? companyId,
    out object diagnostics)
        {
            siteId = null;
            companyId = null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Root MUST be an array per your sample
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                diagnostics = new { container = root.ValueKind.ToString(), note = "Expected root array." };
                return false;
            }

            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                // Keys are snake_case: site_id (string guid), company_id (number)
                if (!TryGetStringCI(item, "site_id", out var sId))
                    continue;

                if (!TryGetNumericOrStringCI(item, "company_id", out var cId))
                    continue;

                siteId = sId;
                companyId = cId;
                diagnostics = new
                {
                    picked = new { site_id = siteId, company_id = companyId },
                    keys = item.EnumerateObject().Select(p => p.Name).ToArray()
                };
                return true;
            }

            diagnostics = new
            {
                message = "Scanned array, but no object contained both site_id and company_id.",
                firstElementPreview = root.GetArrayLength() > 0 ? root[0].ToString() : "(empty)"
            };
            return false;
        }

        public static bool TryGetStringCI(JsonElement obj, string name, out string value)
        {
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (p.Value.ValueKind == JsonValueKind.String)
                    {
                        value = p.Value.GetString() ?? "";
                        return !string.IsNullOrWhiteSpace(value);
                    }
                    var s = p.Value.ToString();
                    value = s;
                    return !string.IsNullOrWhiteSpace(value);
                }
            value = "";
            return false;
        }

        public static bool TryGetNumericOrStringCI(JsonElement obj, string name, out string value)
        {
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value.ValueKind switch
                    {
                        JsonValueKind.Number => p.Value.TryGetInt64(out var i64) ? i64.ToString() :
                                                p.Value.TryGetDouble(out var d) ? ((long)d).ToString() : "",
                        JsonValueKind.String => p.Value.GetString() ?? "",
                        _ => p.Value.ToString()
                    };
                    return !string.IsNullOrWhiteSpace(value);
                }
            value = "";
            return false;
        }

        public static bool TryGetSitesArray(JsonElement root, out JsonElement sitesArray, out object containerInfo)
        {
            sitesArray = default;

            if (root.ValueKind == JsonValueKind.Array)
            {
                containerInfo = new { container = "root is array" };
                sitesArray = root;
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                // Typical wrappers people see in the wild
                var candidateNames = new[] { "sites", "items", "value", "data", "results" };
                foreach (var name in candidateNames)
                {
                    if (TryGetPropertyCI(root, name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        containerInfo = new { container = "object", arrayProperty = name };
                        sitesArray = arr;
                        return true;
                    }
                }

                // As a last resort, pick the FIRST array-typed property
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        containerInfo = new { container = "object", arrayProperty = prop.Name };
                        sitesArray = prop.Value;
                        return true;
                    }
                }

                containerInfo = new
                {
                    container = "object",
                    topLevelKeys = root.EnumerateObject().Select(p => p.Name).ToArray()
                };
                return false;
            }

            containerInfo = new { container = root.ValueKind.ToString() };
            return false;
        }

        public static bool TryGetFirstCompanyId(JsonElement siteObj, out string companyId, out object diag)
        {
            companyId = string.Empty;

            // Common container names
            var containerCandidates = new[] { "companies", "companyList", "company", "entities" };

            foreach (var name in containerCandidates)
            {
                if (TryGetPropertyCI(siteObj, name, out var cVal) && cVal.ValueKind == JsonValueKind.Array)
                {
                    foreach (var comp in cVal.EnumerateArray())
                    {
                        if (comp.ValueKind != JsonValueKind.Object) continue;

                        // Prefer "companyId", then "id"
                        if (TryGetNumericOrStringCI(comp, "companyId", out companyId) ||
                            TryGetNumericOrStringCI(comp, "companyID", out companyId) ||
                            TryGetNumericOrStringCI(comp, "id", out companyId))
                        {
                            diag = new
                            {
                                container = name,
                                companyObjectKeys = comp.EnumerateObject().Select(p => p.Name).ToArray()
                            };
                            return true;
                        }
                    }

                    diag = new { container = name, message = "No object with companyId found." };
                    return false;
                }
            }

            // Sometimes companies are not under a dedicated array; try any array-valued property
            foreach (var prop in siteObj.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var comp in prop.Value.EnumerateArray())
                {
                    if (comp.ValueKind != JsonValueKind.Object) continue;
                    if (TryGetNumericOrStringCI(comp, "companyId", out companyId) ||
                        TryGetNumericOrStringCI(comp, "id", out companyId))
                    {
                        diag = new { container = prop.Name, note = "generic array fallback" };
                        return true;
                    }
                }
            }

            diag = new { message = "No companies array found on site object.", siteKeys = siteObj.EnumerateObject().Select(p => p.Name).ToArray() };
            return false;
        }

        public static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                { value = p.Value; return true; }
            value = default;
            return false;
        }


    }
}
