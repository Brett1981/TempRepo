namespace Sage200Microservice.Services.Shared
{
    /// <summary>
    /// Maps friendly status to Sage enum literal and normalizes Sage enum literal back to friendly.
    /// </summary>
    public static class StatusMapping
    {
        private static readonly Dictionary<string, string> ToEnum = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Live"] = "EnumDocumentStatusLive",
            ["OnHold"] = "EnumDocumentStatusOnHold",
            ["Cancelled"] = "EnumDocumentStatusCancelled",
            ["Canceled"] = "EnumDocumentStatusCancelled",
            ["Completed"] = "EnumDocumentStatusComplete",
            ["Complete"] = "EnumDocumentStatusComplete"
        };

        /// <summary>Converts friendly (Live/OnHold/Cancelled/Completed) to Sage enum literal.</summary>
        public static string ToSageEnum(string friendly)
        {
            if (string.IsNullOrWhiteSpace(friendly))
                throw new ArgumentException("Status is required.", nameof(friendly));

            if (ToEnum.TryGetValue(friendly.Trim(), out var lit))
                return lit;

            // Allow passing the literal through if they already gave a Sage literal
            if (friendly.StartsWith("EnumDocumentStatus", StringComparison.OrdinalIgnoreCase))
                return friendly.Trim();

            throw new ArgumentOutOfRangeException(nameof(friendly),
                "Status must be one of: Live, OnHold, Cancelled, Completed.");
        }

        /// <summary>Converts Sage enum literal (or already-friendly string) back to friendly.</summary>
        public static string NormalizeFromSage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var s = value.Trim();

            const string prefix = "EnumDocumentStatus";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(prefix.Length);
            }

            return s switch
            {
                "Complete" or "Completed" => "Completed",
                "Cancelled" or "Canceled" => "Cancelled",
                "OnHold" or "Held" => "OnHold",
                _ => s // Live or any already-friendly
            };
        }
    }
}
