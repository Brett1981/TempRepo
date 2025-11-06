namespace Sage200Microservice.Services.Configuration
{
    /// <summary>
    /// Strongly-typed "Features:Sop" options section.
    /// </summary>
    public sealed class SopFeaturesOptions
    {
        /// <summary>
        /// When true, the service will publish "sop.order.created" events after successful create.
        /// </summary>
        public bool PublishCreatedEventEnabled { get; set; } = false;

        /// <summary>Stage 1 list endpoint feature flag (kept here for completeness).</summary>
        public bool OrdersListEnabled { get; set; } = true;

        /// <summary>Stage 1 get endpoint feature flag (kept here for completeness).</summary>
        public bool OrdersGetEnabled { get; set; } = true;

        /// <summary>Stage 2 create endpoint feature flag (kept here for completeness).</summary>
        public bool OrdersCreateEnabled { get; set; } = true;
    }
}
