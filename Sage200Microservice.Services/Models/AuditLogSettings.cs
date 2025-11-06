using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Settings for audit logging
    /// </summary>
    public class AuditLogSettings
    {
        /// <summary>
        /// Gets or sets whether audit logging is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log authentication events
        /// </summary>
        public bool LogAuthenticationEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log authorization events
        /// </summary>
        public bool LogAuthorizationEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log data access events
        /// </summary>
        public bool LogDataAccessEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log data modification events
        /// </summary>
        public bool LogDataModificationEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log API key management events
        /// </summary>
        public bool LogApiKeyManagementEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log system events
        /// </summary>
        public bool LogSystemEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log error events
        /// </summary>
        public bool LogErrorEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log HTTP requests
        /// </summary>
        public bool LogHttpRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to log only sensitive endpoints
        /// </summary>
        public bool LogOnlySensitiveEndpoints { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of sensitive endpoints
        /// </summary>
        public List<string> SensitiveEndpoints { get; set; } = new List<string>
        {
            "/api/customers",
            "/api/invoices",
            "/api/apikeys"
        };

        /// <summary>
        /// Gets or sets the list of excluded endpoints
        /// </summary>
        public List<string> ExcludedEndpoints { get; set; } = new List<string>
        {
            "/health",
            "/metrics",
            "/swagger"
        };

        /// <summary>
        /// Gets or sets whether to enable retention
        /// </summary>
        public bool EnableRetention { get; set; } = true;

        /// <summary>
        /// Gets or sets the retention period in days (0 means indefinite)
        /// </summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// Gets or sets whether to mask sensitive data
        /// </summary>
        public bool MaskSensitiveData { get; set; } = true;

        /// <summary>
        /// Gets or sets the list of sensitive fields to mask
        /// </summary>
        public List<string> SensitiveFields { get; set; } = new List<string>
        {
            "password",
            "token",
            "secret",
            "key",
            "apiKey",
            "creditCard",
            "ssn",
            "socialSecurityNumber"
        };

        /// <summary>
        /// Gets or sets the maximum size of the details field in bytes
        /// </summary>
        public int MaxDetailsSize { get; set; } = 10240; // 10 KB

        /// <summary>
        /// Gets or sets the cleanup interval in hours
        /// </summary>
        public int CleanupIntervalHours { get; set; } = 24;

        /// <summary>
        /// If empty/null, all event types are logged.
        /// </summary>
        public List<AuditEventType> EventTypesToLog { get; set; } = new();

        /// <summary>
        /// Default retention (days) to apply to new logs. 0 = never expire.
        /// </summary>
        public int DefaultRetentionDays { get; set; } = 0;
    }
}