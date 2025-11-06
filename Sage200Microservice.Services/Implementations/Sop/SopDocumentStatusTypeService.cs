using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models.Sop;

namespace Sage200Microservice.Services.Implementations.Sop
{
    /// <summary>
    /// Fetches SOP document status types from Sage 200 with a resilient fallback.
    /// This avoids breaking your UI/API when Sage intermittently returns 502.
    /// </summary>
    public sealed class SopDocumentStatusTypeService : ISopDocumentStatusTypeService
    {
        private readonly ISageApiClient _api;
        private readonly ILogger<SopDocumentStatusTypeService> _log;

        // ✅ Static fallback taken from your environment truth:
        private static readonly SopDocumentStatusTypeDto[] Fallback =
        {
            new() { Code = "0", Name = "Live",      Description = "Live" },
            new() { Code = "1", Name = "On hold",   Description = "On hold" },
            new() { Code = "2", Name = "Completed", Description = "Completed" },
            new() { Code = "3", Name = "Disputed",  Description = "Disputed" },
            new() { Code = "4", Name = "Cancelled", Description = "Cancelled" },
            new() { Code = "5", Name = "Draft",     Description = "Draft" },
            new() { Code = "6", Name = "Printed",   Description = "Printed" },
            new() { Code = "7", Name = "Lost",      Description = "Lost" }
        };

        public SopDocumentStatusTypeService(ISageApiClient api, ILogger<SopDocumentStatusTypeService> log)
        {
            _api = api;
            _log = log;
        }

        /// <summary>
        /// Returns document status types from Sage. If Sage 5xx's we degrade to the local fallback.
        /// </summary>
        public async Task<IReadOnlyList<SopDocumentStatusTypeDto>> ListAsync(HttpContext http, CancellationToken ct)
        {
            const string endpoint = "sop_document_status_types";

            try
            {
                var doc = await _api.GetAsync<JsonDocument>(endpoint, ct);

                // Handle either { "value": [...] } or a bare array.
                var list = new List<SopDocumentStatusTypeDto>();
                var root = doc.RootElement;

                var array = root.ValueKind switch
                {
                    JsonValueKind.Object when root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array => v,
                    JsonValueKind.Array => root,
                    _ => default
                };

                if (array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in array.EnumerateArray())
                    {
                        var code = FirstString(el, "code", "enum_code", "document_status", "status", "id", "value");
                        var name = FirstString(el, "name", "display_name", "description", "text");
                        var desc = FirstString(el, "description", "notes", "help_text");

                        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                            name = NormalizeFriendly(code!);

                        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(name))
                        {
                            list.Add(new SopDocumentStatusTypeDto
                            {
                                Code = code ?? string.Empty,
                                Name = name ?? string.Empty,
                                Description = desc
                            });
                        }
                    }

                    if (list.Count > 0) return list;
                }

                _log.LogWarning("Unexpected payload shape for {Endpoint}. Falling back to built-in status types.", endpoint);
                return Fallback;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode >= 500)
            {
                _log.LogWarning(ex, "Sage returned {Status} for {Endpoint}. Using fallback document status types.", ex.StatusCode, endpoint);
                return Fallback;
            }
        }

        private static string? FirstString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                    if (prop.ValueKind == JsonValueKind.Number) return prop.ToString();
                }
            }
            return null;
        }

        private static string NormalizeFriendly(string value)
        {
            var s = value.Trim();
            const string prefix = "EnumDocumentStatus";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                s = s[prefix.Length..];

            return s switch
            {
                "Complete" or "Completed" => "Completed",
                "Cancelled" or "Canceled" => "Cancelled",
                "OnHold" or "Held" => "On hold",
                _ => s
            };
        }
    }
}
