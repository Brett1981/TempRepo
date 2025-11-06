namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Configuration options for API key rotation
    /// </summary>
    public class ApiKeyRotationOptions
    {
        /// <summary>
        /// Gets or sets whether API key rotation is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum age of an API key in days before it should be rotated
        /// </summary>
        public int MaxKeyAgeInDays { get; set; } = 90;

        /// <summary>
        /// Gets or sets the grace period in days during which the old key remains valid
        /// </summary>
        public int GracePeriodDays { get; set; } = 7;

        /// <summary>
        /// Gets or sets the interval in hours between rotation checks
        /// </summary>
        public int CheckIntervalHours { get; set; } = 24;

        /// <summary>
        /// Gets or sets whether to notify clients about key rotation
        /// </summary>
        public bool NotifyClients { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of days before expiration to send a notification
        /// </summary>
        public int NotifyDaysBeforeExpiration { get; set; } = 14;
    }
}