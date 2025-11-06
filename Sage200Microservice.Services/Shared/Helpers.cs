// ================================================================
// File: Services/Shared/Helpers.cs
// Project: Sage200Microservice.Services
// Purpose: Central OData helpers reused across services (non-breaking), augmented to expose methods
// required by SopOrderService. ================================================================
using Microsoft.AspNetCore.Hosting;
using System.Dynamic;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sage200Microservice.Services.Shared
{
    public static class OData
    {
        // ------------------------- Existing helpers -------------------------
        public static string S(string s) => (s ?? string.Empty).Replace("'", "''");
        public static string D(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ssZ");
        public static string E(DateTime d) => $"{D(d)}"; // direct literal for eq/ge/le

        public static string JoinAnd(IEnumerable<string> parts)
            => string.Join(" and ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        public static string Build(string resource, string? filter, string? orderby, int top, int skip, bool includeCount)
        {
            var qp = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter)) qp.Add($"$filter={filter}");
            if (!string.IsNullOrWhiteSpace(orderby)) qp.Add($"$orderby={orderby}");
            qp.Add($"$top={Math.Max(1, top)}");
            qp.Add($"$skip={Math.Max(0, skip)}");
            if (includeCount) qp.Add("$count=true");
            return $"{resource}?{string.Join("&", qp)}";
        }

        public static (IReadOnlyList<T> Items, int Total) MaterializePagedFlexible<T>(JsonDocument doc)
        {
            var root = doc.RootElement;

            // A) { "value": [...], "@odata.count": N }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var valueArr) && valueArr.ValueKind == JsonValueKind.Array)
            {
                var items = valueArr.EnumerateArray().Select(e => JsonSerializer.Deserialize<T>(e.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!).ToList();
                var total = root.TryGetProperty("@odata.count", out var cnt) && cnt.ValueKind == JsonValueKind.Number ? cnt.GetInt32() : items.Count;
                return (items, total);
            }

            // B) { "items": [...], "count": N }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var itemsArr) && itemsArr.ValueKind == JsonValueKind.Array)
            {
                var items = itemsArr.EnumerateArray().Select(e => JsonSerializer.Deserialize<T>(e.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!).ToList();
                var total = root.TryGetProperty("count", out var cnt) && cnt.ValueKind == JsonValueKind.Number ? cnt.GetInt32() : items.Count;
                return (items, total);
            }

            // C) Raw array
            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = root.EnumerateArray().Select(e => JsonSerializer.Deserialize<T>(e.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!).ToList();
                return (items, items.Count);
            }

            return (Array.Empty<T>(), 0);
        }

        public static T? MaterializeSingle<T>(JsonDocument doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                // direct object
                if (root.TryGetProperty("id", out _))
                    return JsonSerializer.Deserialize<T>(root.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // { items: [ {..}, .. ] }
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                    return JsonSerializer.Deserialize<T>(items[0].GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // { value: [ {..} ] }
                if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
                    return JsonSerializer.Deserialize<T>(value[0].GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                return JsonSerializer.Deserialize<T>(root[0].GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return default;
        }

        public static string EscapeODataString(string value)
           => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        public static bool LooksLikeValidFilter(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return false;
            var s = f.Trim();
            if (s.Contains(" contains(", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("contains(", StringComparison.OrdinalIgnoreCase) ||
                s.Contains(" substringof(", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("substringof(", StringComparison.OrdinalIgnoreCase))
                return true;

            var hasOp = Regex.IsMatch(s, @"\b(eq|ne|gt|ge|lt|le)\b", RegexOptions.IgnoreCase);
            return hasOp;
        }

        public static string? ToJson(object obj) =>
            obj == null ? null
                        : JsonSerializer.Serialize(obj, new JsonSerializerOptions
                        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // ------------------------- NEW: required surface -------------------------

        /// <summary>
        /// Builds an OData URL with reserved params plus any extra query pairs. Improvements:
        /// - Defaults $top to 50 when not provided or &lt;= 0 (so a bare call still returns a page)
        /// - Always adds $count unless explicitly overridden in <paramref name="extra"/> ($count
        ///   wins if present)
        /// - Safely merges a caller-supplied <paramref name="filter"/> with any extra["$filter"]
        ///   using AND
        /// - Deduplicates reserved params from <paramref name="extra"/> ($filter/$orderby/$top/$skip/$count)
        /// </summary>
        public static string BuildUrl(
            string basePath,
            string? filter,
            string? orderBy,
            int? top,
            int? skip,
            IDictionary<string, string?>? extra = null)
        {
            // Merge any extra-provided $filter with the primary filter
            string? extraFilter = null;
            if (extra is not null && extra.TryGetValue("$filter", out var ef) && !string.IsNullOrWhiteSpace(ef))
            {
                extraFilter = ef;
            }

            var mergedFilter = MergeFilters(filter, extraFilter);

            var qp = new List<string>();

            if (!string.IsNullOrWhiteSpace(mergedFilter))
                qp.Add($"$filter={Uri.EscapeDataString(mergedFilter)}");

            if (!string.IsNullOrWhiteSpace(orderBy))
                qp.Add($"$orderby={Uri.EscapeDataString(orderBy)}");

            // Default page size if none provided (prevents "empty response" when callers pass nothing)
            var effectiveTop = (top.HasValue && top.Value > 0) ? top.Value : 50;
            qp.Add($"$top={Math.Max(1, effectiveTop)}");

            if (skip.HasValue && skip.Value > 0)
                qp.Add($"$skip={Math.Max(0, skip.Value)}");

            // Respect explicit $count in "extra"; otherwise force $count=true for totals
            var countOverridden = extra is not null && extra.ContainsKey("$count");
            if (countOverridden)
            {
                var val = extra!["$count"];
                qp.Add($"$count={Uri.EscapeDataString(string.IsNullOrWhiteSpace(val) ? "true" : val!)}");
            }
            else
            {
                qp.Add("$count=true");
            }

            // Append any non-reserved extras
            if (extra is not null)
            {
                foreach (var kv in extra)
                {
                    if (kv.Value is null) continue;

                    // Skip duplicates of reserved params; they are already handled
                    if (kv.Key.Equals("$filter", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("$orderby", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("$top", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("$skip", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("$count", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    qp.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
                }
            }

            return qp.Count == 0 ? basePath : $"{basePath}?{string.Join("&", qp)}";
        }

        /// <summary>
        /// Combines two OData filter fragments with an AND, adding parentheses for safety. If
        /// either is null/empty, returns the other.
        /// </summary>
        private static string? MergeFilters(string? a, string? b)
        {
            var hasA = !string.IsNullOrWhiteSpace(a);
            var hasB = !string.IsNullOrWhiteSpace(b);

            if (hasA && hasB) return $"({a}) and ({b})";
            if (hasA) return a;
            if (hasB) return b;
            return null;
        }

        /// <summary>
        /// Reads a page (items + total) from a JSON/OData payload; strongly typed.
        /// </summary>
        public static Task<(List<T> items, int total)> ReadPageAsync<T>(JsonDocument doc, CancellationToken _)
        {
            var (items, total) = MaterializePagedFlexible<T>(doc);
            return Task.FromResult((items.ToList(), total));
        }

        /// <summary>
        /// Overload that accepts HttpResponseMessage for compatibility.
        /// </summary>
        public static async Task<(List<T> items, int total)> ReadPageAsync<T>(HttpResponseMessage response, CancellationToken ct)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc is null) return (new List<T>(), 0);
            return await ReadPageAsync<T>(doc, ct);
        }

        /// <summary>
        /// Reads a single item (strongly typed).
        /// </summary>
        public static Task<T?> ReadSingleAsync<T>(JsonDocument doc, CancellationToken _) =>
            Task.FromResult(MaterializeSingle<T>(doc));

        /// <summary>
        /// Overload that accepts HttpResponseMessage for compatibility.
        /// </summary>
        public static async Task<T?> ReadSingleAsync<T>(HttpResponseMessage response, CancellationToken ct)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc is null) return default;
            return MaterializeSingle<T>(doc);
        }

        /// <summary>
        /// Reads a single dynamic item; returns an ExpandoObject with top-level fields. Ensures
        /// properties like 'id' and 'reference' can be accessed as created.id.
        /// </summary>
        public static async Task<dynamic?> ReadSingleDynamicAsync(JsonDocument doc, CancellationToken _)
        {
            return ToDynamicSingle(doc);
        }

        /// <summary>
        /// Overload that accepts HttpResponseMessage.
        /// </summary>
        public static async Task<dynamic?> ReadSingleDynamicAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            return doc is null ? null : ToDynamicSingle(doc);
        }

        private static dynamic? ToDynamicSingle(JsonDocument doc)
        {
            JsonElement obj = default;
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out _)) obj = root;
                else if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                    obj = items[0];
                else if (root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                    obj = val[0];
            }
            else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                obj = root[0];
            }

            if (obj.ValueKind != JsonValueKind.Object) return null;

            IDictionary<string, object?> exp = new ExpandoObject();
            foreach (var p in obj.EnumerateObject())
            {
                exp[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l :
                                            p.Value.TryGetDouble(out var d) ? d : (object?)p.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText()
                };
            }
            return (ExpandoObject)exp;
        }

        public static string EscapeOData(string s) => (s ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        // Misc existing helpers used elsewhere in the project
        public static string SanitizeConnectionString(string cs)
        {
            if (string.IsNullOrWhiteSpace(cs)) return cs;
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries)
                          .Select(p =>
                          {
                              var kv = p.Split('=', 2);
                              if (kv.Length != 2) return p;
                              var key = kv[0].Trim().ToLowerInvariant();
                              if (key is "password" or "pwd" or "user id" or "uid")
                                  return $"{kv[0]}=***";
                              return p;
                          });
            return string.Join(';', parts);
        }

        public static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(System.Net.IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public static string SanitizeSql(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) return statement ?? string.Empty;

            // very simple redaction:
            var redacted = Regex.Replace(statement, @"'([^']|'')*'", "'?'");
            redacted = Regex.Replace(redacted, @"(?i)(password\s*=\s*)[^;\s]+", "$1***");
            return redacted;
        }

        public static int GetHttpPortOverrideOrConfigured(WebHostBuilderContext ctx, int configured)
        {
            var fromEnv = Environment.GetEnvironmentVariable("S200_HTTP_PORT");
            if (!string.IsNullOrWhiteSpace(fromEnv) && int.TryParse(fromEnv, out var p) && p > 0 && p < 65536)
                return p;
            return configured;
        }

        private static readonly JsonSerializerOptions CaseInsensitive = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Materializes a paged response into a strongly-typed list and total.
        /// </summary>
        public static (List<T> Items, int? TotalCount) MaterializePaged<T>(JsonDocument doc)
        {
            var items = new List<T>();
            int? total = null;
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("@odata.count", out var cnt) && cnt.ValueKind == JsonValueKind.Number)
                    total = cnt.TryGetInt32(out var t32) ? t32 : (cnt.TryGetInt64(out var t64) ? (int?)t64 : null);

                if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var item = JsonSerializer.Deserialize<T>(el.GetRawText(), CaseInsensitive);
                        if (item is not null) items.Add(item);
                    }
                    return (items, total);
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<T>(el.GetRawText(), CaseInsensitive);
                    if (item is not null) items.Add(item);
                }
                return (items, total);
            }

            // Fallback: single object treated as one-item page.
            var single = JsonSerializer.Deserialize<T>(root.GetRawText(), CaseInsensitive);
            if (single is not null) items.Add(single);
            return (items, total ?? items.Count);
        }

        /// <summary>
        /// Materializes a single object from either { "value": [ {...} ] } or a plain object.
        /// Returns null if no object is present.
        /// </summary>
        public static object? MaterializeSingle(JsonDocument doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in v.EnumerateArray()) // first element wins
                        return JsonDocument.Parse(el.GetRawText()).RootElement.Clone();
                    return null;
                }

                return root.Clone();
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                    return JsonDocument.Parse(el.GetRawText()).RootElement.Clone();
                return null;
            }

            return null;
        }

        /// <summary>
        /// Convert a JsonElement or POCO into an ExpandoObject for dynamic access.
        /// </summary>
        private static ExpandoObject ToExpando(object obj)
        {
            if (obj is JsonElement je)
            {
                return JsonToExpando(je);
            }

            var json = JsonSerializer.Serialize(obj, CaseInsensitive);
            var root = JsonDocument.Parse(json).RootElement;
            return JsonToExpando(root);
        }

        private static ExpandoObject JsonToExpando(JsonElement elem)
        {
            var exp = new ExpandoObject();
            var dict = (IDictionary<string, object?>)exp;

            if (elem.ValueKind != JsonValueKind.Object)
                return exp;

            foreach (var prop in elem.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Object => JsonToExpando(prop.Value),
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select(JsonToExpando).ToList(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var i64)
                        ? i64
                        : (prop.Value.TryGetDouble(out var dbl) ? dbl : prop.Value.GetRawText()),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }

            return exp;
        }
    }
    public static class Helpers
    {
        /// <summary>
        /// Truncates text to <paramref name="max"/> characters. Appends <paramref name="suffix"/>
        /// if truncated. Safe for nulls (returns empty string).
        /// </summary>
        public static string Truncate(string? s, int max, string suffix = "...")
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (max <= 0) return "";
            if (s.Length <= max) return s;
            var cut = Math.Max(0, max - suffix.Length);
            return cut <= 0 ? s.Substring(0, max) : s.Substring(0, cut) + suffix;
        }
    }
}