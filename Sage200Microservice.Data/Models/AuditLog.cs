using System.Text.Json;

namespace Sage200Microservice.Data.Models
{
    /// <summary>
    /// Represents an audit log entry
    /// </summary>
    public class AuditLog
    {
        /// <summary>
        /// Gets or sets the unique identifier for the audit log
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the event occurred
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the type of event
        /// </summary>
        public AuditEventType EventType { get; set; }

        /// <summary>
        /// Gets or sets the category of the event
        /// </summary>
        public AuditEventCategory Category { get; set; }

        /// <summary>
        /// Gets or sets the severity level of the event
        /// </summary>
        public AuditEventSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the user who performed the action
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the client application (API key)
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets the IP address of the client
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Gets or sets the resource being accessed or modified
        /// </summary>
        public string Resource { get; set; }

        /// <summary>
        /// Gets or sets the action being performed
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Gets or sets the outcome of the action
        /// </summary>
        public AuditEventStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the human-readable description of the event
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the additional details about the event (JSON)
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Gets or sets the identifier to correlate related events
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Gets or sets the HTTP method (if applicable)
        /// </summary>
        public string HttpMethod { get; set; }

        /// <summary>
        /// Gets or sets the URL path (if applicable)
        /// </summary>
        public string UrlPath { get; set; }

        /// <summary>
        /// Gets or sets the HTTP status code (if applicable)
        /// </summary>
        public int? HttpStatusCode { get; set; }

        /// <summary>
        /// Gets or sets the duration of the operation in milliseconds (if applicable)
        /// </summary>
        public long? DurationMs { get; set; }

        /// <summary>
        /// Gets or sets the user agent of the client (if applicable)
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Gets or sets the reference ID of the affected entity (if applicable)
        /// </summary>
        public string? ReferenceId { get; set; }

        /// <summary>
        /// Gets or sets the name of the affected entity (if applicable)
        /// </summary>
        public string? ReferenceName { get; set; }

        /// <summary>
        /// Gets or sets the previous state of the entity (JSON, if applicable)
        /// </summary>
        public string? PreviousState { get; set; }

        /// <summary>
        /// Gets or sets the new state of the entity (JSON, if applicable)
        /// </summary>
        public string? NewState { get; set; }

        /// <summary>
        /// Gets or sets the retention period in days (0 means indefinite)
        /// </summary>
        public int RetentionDays { get; set; }

        /// <summary>
        /// Gets or sets the expiration date of the audit log
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Sets the details from an object
        /// </summary>
        /// <param name="details"> The details object </param>
        public void SetDetails(object details)
        {
            if (details != null)
            {
                Details = JsonSerializer.Serialize(details, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
        }

        /// <summary>
        /// Gets the details as a typed object
        /// </summary>
        /// <typeparam name="T"> The type of the details object </typeparam>
        /// <returns> The details object </returns>
        public T GetDetails<T>() where T : class
        {
            if (string.IsNullOrEmpty(Details))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(Details, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }

    /// <summary>
    /// Represents the type of audit event
    /// </summary>
    public enum AuditEventType
    {
        /// <summary>
        /// Authentication event
        /// </summary>
        Authentication,

        /// <summary>
        /// Authorization event
        /// </summary>
        Authorization,

        /// <summary>
        /// Data access event
        /// </summary>
        DataAccess,

        /// <summary>
        /// Data modification event
        /// </summary>
        DataModification,

        /// <summary>
        /// System event
        /// </summary>
        System,

        /// <summary>
        /// API key management event
        /// </summary>
        ApiKeyManagement,

        /// <summary>
        /// Configuration change event
        /// </summary>
        ConfigurationChange,

        /// <summary>
        /// Error event
        /// </summary>
        Error
    }

    /// <summary>
    /// Represents the category of audit event
    /// </summary>
    public enum AuditEventCategory
    {
        /// <summary>
        /// Security-related event
        /// </summary>
        Security,

        /// <summary>
        /// Data-related event
        /// </summary>
        Data,

        /// <summary>
        /// System-related event
        /// </summary>
        System,

        /// <summary>
        /// Business-related event
        /// </summary>
        Business
    }

    /// <summary>
    /// Represents the severity level of audit event
    /// </summary>
    public enum AuditEventSeverity
    {
        /// <summary>
        /// Informational event
        /// </summary>
        Info,

        /// <summary>
        /// Warning event
        /// </summary>
        Warning,

        /// <summary>
        /// Error event
        /// </summary>
        Error,

        /// <summary>
        /// Critical event
        /// </summary>
        Critical
    }

    /// <summary>
    /// Represents the status of audit event
    /// </summary>
    public enum AuditEventStatus
    {
        /// <summary>
        /// Successful event
        /// </summary>
        Success,

        /// <summary>
        /// Failed event
        /// </summary>
        Failure,

        /// <summary>
        /// Denied event
        /// </summary>
        Denied,

        /// <summary>
        /// Event in progress
        /// </summary>
        InProgress
    }
}