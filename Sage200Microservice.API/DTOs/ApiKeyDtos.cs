using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for API key responses
    /// </summary>
    public class ApiKeyResponseDto
    {
        /// <summary>
        /// Gets or sets the API key ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the API key value
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the client name
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// Gets or sets the creation date
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the expiration date
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets whether the API key is active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the last used date
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        /// <summary>
        /// Gets or sets the allowed IP addresses
        /// </summary>
        public string AllowedIpAddresses { get; set; }

        /// <summary>
        /// Gets or sets the API key version
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Gets or sets whether the API key has a previous key
        /// </summary>
        public bool HasPreviousKey { get; set; }

        /// <summary>
        /// Gets or sets the expiration date of the previous key
        /// </summary>
        public DateTime? PreviousKeyExpiresAt { get; set; }
    }

    /// <summary>
    /// DTO for creating an API key
    /// </summary>
    public class CreateApiKeyRequestDto
    {
        /// <summary>
        /// Gets or sets the client name
        /// </summary>
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string ClientName { get; set; }

        /// <summary>
        /// Gets or sets the expiration date
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets the allowed IP addresses
        /// </summary>
        public string AllowedIpAddresses { get; set; }
    }

    /// <summary>
    /// DTO for updating an API key
    /// </summary>
    public class UpdateApiKeyRequestDto
    {
        /// <summary>
        /// Gets or sets the client name
        /// </summary>
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string ClientName { get; set; }

        /// <summary>
        /// Gets or sets the expiration date
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets whether the API key is active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the allowed IP addresses
        /// </summary>
        public string AllowedIpAddresses { get; set; }
    }

    /// <summary>
    /// DTO for rotating an API key
    /// </summary>
    public class RotateApiKeyRequestDto
    {
        /// <summary>
        /// Gets or sets the grace period in days
        /// </summary>
        [Range(1, 30)]
        public int GracePeriodDays { get; set; } = 7;
    }
}