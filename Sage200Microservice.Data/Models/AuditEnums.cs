namespace Sage200Microservice.Data.Models;

/// <summary>
/// Audit event type enumeration
/// </summary>
public enum AuditEventType
{
    ApiRequest,
    ApiResponse,
    KafkaMessageReceived,
    KafkaMessagePublished,
    CustomerCreated,
    CustomerUpdated,
    InvoiceCreated,
    InvoiceUpdated,
    PaymentSync,
    AuthTokenRefresh,
    ValidationFailure,
    SystemError
}

/// <summary>
/// Audit event category enumeration
/// </summary>
public enum AuditEventCategory
{
    Business,
    System,
    Security
}

/// <summary>
/// Audit event severity enumeration
/// </summary>
public enum AuditEventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Audit event status enumeration
/// </summary>
public enum AuditEventStatus
{
    Success,
    Failure,
    InProgress,
    Pending
}
