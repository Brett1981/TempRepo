namespace Sage200Microservice.Data.Models
{
    /// <summary>
    /// Represents an API key for client authentication
    /// </summary>
    public class ApiKey
    {
        /// <summary>
        /// The unique identifier for the API key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The key value used for authentication
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The name of the client or application using this key
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// The date and time when the key was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The date and time when the key expires (null means no expiration)
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Indicates whether the key is active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// The date and time when the key was last used
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        /// <summary>
        /// Comma-separated list of allowed IP addresses or CIDR ranges
        /// </summary>
        public string AllowedIpAddresses { get; set; } = "[]"; // store JSON array as string

        /// <summary>
        /// The version of the key (for rotation purposes)
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// The previous key value (during rotation grace period)
        /// </summary>
        public string? PreviousKey { get; set; }

        /// <summary>
        /// The date and time when the previous key expires
        /// </summary>
        public DateTime? PreviousKeyExpiresAt { get; set; }

        /// <summary>
        /// Checks if the API key is valid (active and not expired)
        /// </summary>
        /// <returns> True if the key is valid, false otherwise </returns>
        public bool IsValid()
        {
            return IsActive && (!ExpiresAt.HasValue || ExpiresAt.Value > DateTime.UtcNow);
        }

        /// <summary>
        /// Checks if the previous key is still valid during the grace period
        /// </summary>
        /// <returns> True if the previous key is valid, false otherwise </returns>
        public bool IsPreviousKeyValid()
        {
            return IsActive &&
                   !string.IsNullOrEmpty(PreviousKey) &&
                   PreviousKeyExpiresAt.HasValue &&
                   PreviousKeyExpiresAt.Value > DateTime.UtcNow;
        }

        public DateTime? GracePeriodEnd { get; set; }
    }
}