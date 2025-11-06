namespace Sage200Microservice.Services.Models.Sop
{
    /// <summary>
    /// Canonical DTO for a SOP document status type.
    /// We expose:
    /// - Code: the system/enum code returned by Sage (e.g., "EnumDocumentStatusLive" or "1")
    /// - Name: a friendly display name (e.g., "Live")
    /// - Description: longer text if provided by Sage (optional)
    /// </summary>
    public sealed class SopDocumentStatusTypeDto
    {
        /// <summary>System/enum code for the status (may be an enum literal or a numeric code as a string).</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Friendly display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional extra description provided by Sage (if any).</summary>
        public string? Description { get; set; }
    }
}
