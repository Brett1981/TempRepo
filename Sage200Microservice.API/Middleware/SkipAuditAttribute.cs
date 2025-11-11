using System;

namespace Sage200Microservice.API.Middleware
{
    /// <summary>
    /// Decorate controllers/actions that must not be audit-logged
    /// (e.g., audit browsing endpoints, metrics, dashboards).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class SkipAuditAttribute : Attribute { }
}
