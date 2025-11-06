using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// Single external reference supplied by a calling application.
    /// </summary>
    public sealed class ExternalRefItemDto
    {
        /// <summary>
        /// Optional explicit AppId; if omitted, the server resolves it from "X-Api-Key".
        /// </summary>
        public int? AppId { get; set; }

        /// <summary>
        /// The client application's identifier for the entity (e.g., "BRE001").
        /// </summary>
        [Required, MaxLength(200)]
        public string ExternalRef { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wrapper for optional list binding.
    /// </summary>
    public sealed class ExternalRefsDto
    {
        /// <summary>
        /// Optional collection of external references.
        /// </summary>
        public List<ExternalRefItemDto>? Items { get; set; }
    }
}
