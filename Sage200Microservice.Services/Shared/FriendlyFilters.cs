using Sage200Microservice.Services.Models.Sop;
using System.Text;
using System.Text.RegularExpressions;

namespace Sage200Microservice.Services.Shared
{
    /// <summary>
    /// Translates friendly query parameters into an OData $filter string, and can apply them to a URL.
    /// IMPORTANT for your site:
    /// - <c>document_status</c> is an integer code:
    ///   0=Live, 1=On hold, 2=Completed, 3=Disputed, 4=Cancelled, 5=Draft, 6=Printed, 7=Lost.
    /// </summary>
    public static class FriendlyFilters
    {
        public const int StatusLive = 0;
        public const int StatusOnHold = 1;
        public const int StatusCompleted = 2;
        public const int StatusDisputed = 3;
        public const int StatusCancelled = 4;
        public const int StatusDraft = 5;
        public const int StatusPrinted = 6;
        public const int StatusLost = 7;

        private const string EnumQName = "Sage.Accounting.OrderProcessing.DocumentStatusEnum";

        private static readonly Dictionary<string, int> StatusCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Live"] = StatusLive,
            ["On hold"] = StatusOnHold,
            ["OnHold"] = StatusOnHold,
            ["Completed"] = StatusCompleted,
            ["Complete"] = StatusCompleted,
            ["Disputed"] = StatusDisputed,
            ["Cancelled"] = StatusCancelled,
            ["Canceled"] = StatusCancelled,
            ["Draft"] = StatusDraft,
            ["Printed"] = StatusPrinted,
            ["Lost"] = StatusLost
        };
        private static readonly Dictionary<int, string> CodeToEnumToken = new()
        {
            [0] = "Live",
            [1] = "OnHold",
            [2] = "Completed",
            [3] = "Disputed",
            [4] = "Cancelled",
            [5] = "Draft",
            [6] = "Printed",
            [7] = "Lost"
        };

        private static bool TryGetEnumToken(string input, out string fullyQualifiedEnumLiteral)
        {
            // numeric -> enum
            if (int.TryParse(input, out var code) && CodeToEnumToken.TryGetValue(code, out var name))
            {
                fullyQualifiedEnumLiteral = $"{EnumQName}'{name}'";
                return true;
            }

            // friendly text -> code -> enum
            if (StatusCodeMap.TryGetValue(input.Trim(), out var mapped) &&
                CodeToEnumToken.TryGetValue(mapped, out var mappedName))
            {
                fullyQualifiedEnumLiteral = $"{EnumQName}'{mappedName}'";
                return true;
            }

            fullyQualifiedEnumLiteral = default!;
            return false;
        }

        // Builds:  document_status eq Enum'X' or document_status eq Enum'Y' ...
        public static string BuildEnumWhitelist(IEnumerable<int> codes)
        {
            var parts = new List<string>();
            foreach (var c in codes)
            {
                if (CodeToEnumToken.TryGetValue(c, out var name))
                    parts.Add($"document_status eq {EnumQName}'{name}'");
            }
            return string.Join(" or ", parts);
        }

        public static string BuildEnumEquality(int code)
        {
            if (!CodeToEnumToken.TryGetValue(code, out var name))
                throw new KeyNotFoundException($"Unknown document_status code: {code}");
            return $"document_status eq {EnumQName}'{name}'";
        }


        /// <summary>
        /// Composes an OData $filter by AND-ing friendly constraints with any existing filter.
        /// Emits numeric predicates against <c>document_status</c>.
        /// </summary>
        public static string? Compose(
            string? existingFilter,
            long? customerId,
            string? orderNo,
            string? status,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var parts = new List<string>();

            if (customerId.HasValue && customerId.Value > 0)
                parts.Add($"customer_id eq {customerId.Value}");

            if (!string.IsNullOrWhiteSpace(orderNo))
                parts.Add($"order_no eq '{OData.EscapeODataString(orderNo!)}'");

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (TryGetEnumToken(status!, out var enumLiteral))
                {
                    parts.Add($"document_status eq {enumLiteral}");
                }
                else
                {
                    // Last-resort literal (kept for back-compat if upstream ever accepts it)
                    parts.Add($"document_status eq '{OData.EscapeODataString(status!)}'");
                }
            }

            if (fromDate.HasValue)
                parts.Add($"order_date ge {OData.E(fromDate.Value)}");

            if (toDate.HasValue)
                parts.Add($"order_date le {OData.E(toDate.Value)}");

            var friendly = string.Join(" and ", parts);

            if (string.IsNullOrWhiteSpace(existingFilter))
                return string.IsNullOrWhiteSpace(friendly) ? null : friendly;

            if (string.IsNullOrWhiteSpace(friendly))
                return existingFilter!;

            return $"({existingFilter}) and ({friendly})";
        }


        /// <summary>
        /// (Kept for reference) Exclusion-based outstanding clause:
        /// NOT Completed (2), NOT Cancelled (4), NOT Lost (7).
        /// </summary>
        public static string AppendOutstandingExclude(string? existingFilter)
        {
            const string clause =
                "document_status ne 2 and document_status ne 4 and document_status ne 7";

            if (string.IsNullOrWhiteSpace(existingFilter)) return clause;
            return $"({existingFilter}) and ({clause})";
        }

        /// <summary>
        /// Applies friendly filters by rebuilding the URL's $filter parameter (replaces if present).
        /// </summary>
        public static string ApplyToUrl(string url, SopOrderQuery query)
        {
            // Split base and querystring
            var qIndex = url.IndexOf('?', StringComparison.Ordinal);
            var basePath = qIndex >= 0 ? url[..qIndex] : url;
            var qs = qIndex >= 0 ? url[(qIndex + 1)..] : "";

            // Parse into dict (first wins)
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(qs))
            {
                foreach (var part in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=', StringComparison.Ordinal);
                    if (eq < 0) continue;
                    var k = Uri.UnescapeDataString(part[..eq]);
                    var v = Uri.UnescapeDataString(part[(eq + 1)..]);
                    if (!dict.ContainsKey(k)) dict[k] = v;
                }
            }

            dict.TryGetValue("$filter", out var existingFromUrl);

            // Build friendly filter (without passing existing)
            var friendlyOnly = Compose(
                existingFilter: null,
                customerId: query.CustomerId,
                orderNo: query.OrderNo,
                status: query.Status,
                fromDate: query.FromDate,
                toDate: query.ToDate);

            // Merge and replace
            var merged = MergeFilters(existingFromUrl, friendlyOnly);
            if (string.IsNullOrWhiteSpace(merged))
                dict.Remove("$filter");
            else
                dict["$filter"] = merged;

            // Rebuild querystring
            var pairs = dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
            var rebuilt = string.Join("&", pairs);

            return string.IsNullOrEmpty(rebuilt) ? basePath : $"{basePath}?{rebuilt}";
        }

        private static string? MergeFilters(string? a, string? b)
        {
            var hasA = !string.IsNullOrWhiteSpace(a);
            var hasB = !string.IsNullOrWhiteSpace(b);

            if (hasA && hasB) return $"({a}) and ({b})";
            if (hasA) return a;
            if (hasB) return b;
            return null;
        }

        private static bool TryGetStatusCode(string input, out int code)
        {
            if (int.TryParse(input, out code)) return true;
            if (StatusCodeMap.TryGetValue(input.Trim(), out code)) return true;
            code = default;
            return false;
        }

        // === Added: Outstanding SOP helpers and status-filter guards ===

        // Our Outstanding set as enum symbols (single block we inject into $filter)
        private static readonly string[] OutstandingEnumSymbols = new[]
        {
        "Sage.Accounting.OrderProcessing.DocumentStatusEnum'Live'",
        "Sage.Accounting.OrderProcessing.DocumentStatusEnum'OnHold'",
        "Sage.Accounting.OrderProcessing.DocumentStatusEnum'Disputed'",
        "Sage.Accounting.OrderProcessing.DocumentStatusEnum'Draft'",
        "Sage.Accounting.OrderProcessing.DocumentStatusEnum'Printed'"
    };

        // Numeric codes only used for fallback chunking (never added to the main OData request)
        public static readonly IReadOnlyList<int> OutstandingCodes = new[] { 0, 1, 3, 5, 6 };

        /// <summary>
        /// Inject a single enum-based Outstanding status predicate.
        /// If an existing document_status predicate is already present, this is a no-op.
        /// </summary>
        public static string AppendOutstandingWhitelist(string? existing)
        {
            var block = "(document_status eq " + string.Join(" or document_status eq ", OutstandingEnumSymbols) + ")";
            if (ContainsDocumentStatus(existing))
                return existing ?? string.Empty;

            return string.IsNullOrWhiteSpace(existing)
                ? block
                : $"({existing}) and {block}";
        }

        /// <summary>Detect whether any document_status predicate already exists.</summary>
        public static bool ContainsDocumentStatus(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return false;
            return Regex.IsMatch(filter, @"\bdocument_status\s+(eq|in)\b", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Heuristic: does the filter contain the enum-based Outstanding block?
        /// (Used to decide fallback codes when the controller does not supply a numeric list.)
        /// </summary>
        public static bool ContainsOutstandingEnumBlock(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return false;
            return Regex.IsMatch(filter, @"DocumentStatusEnum'(Live|OnHold|Disputed|Draft|Printed)'", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Remove any document_status predicate (including surrounding parens/connectors) so we can
        /// safely re-append a different status clause elsewhere (e.g., per-status fallback).
        /// </summary>
        public static string RemoveDocumentStatus(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return string.Empty;

            var f = filter;

            // Remove parenthesized blocks e.g. (document_status ... )
            f = Regex.Replace(f, @"\(\s*document_status\s+(?:eq|in)\s*[^)]*\)", "", RegexOptions.IgnoreCase);

            // Remove remaining inline clauses (with optional chained ORs)
            f = Regex.Replace(
                f,
                @"(?:(?:\s+(?:and|or)\s+)?)document_status\s+(?:eq|in)\s+[^\s\)]+(?:\s+or\s+document_status\s+(?:eq|in)\s+[^\s\)]+)*",
                "",
                RegexOptions.IgnoreCase
            );

            // Tidy: empty parens and stray connectors
            f = Regex.Replace(f, @"\(\s*\)", "", RegexOptions.IgnoreCase);
            f = Regex.Replace(f, @"\s+(?:and|or)\s*(?=$|\))", "", RegexOptions.IgnoreCase);
            f = Regex.Replace(f, @"(?<=\()\s+(?:and|or)\s+", "", RegexOptions.IgnoreCase);

            return f.Trim();
        }
    }
}
