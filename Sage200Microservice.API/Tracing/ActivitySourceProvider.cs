using System.Diagnostics;

namespace Sage200Microservice.API.Tracing
{
    /// <summary>
    /// Provider for activity sources used in distributed tracing
    /// </summary>
    public static class ActivitySourceProvider
    {
        /// <summary>
        /// Activity source for the API layer
        /// </summary>
        public static readonly ActivitySource ApiSource = new ActivitySource("Sage200Microservice.API", "1.0.0");

        /// <summary>
        /// Activity source for the Services layer
        /// </summary>
        public static readonly ActivitySource ServicesSource = new ActivitySource("Sage200Microservice.Services", "1.0.0");

        /// <summary>
        /// Activity source for the Data layer
        /// </summary>
        public static readonly ActivitySource DataSource = new ActivitySource("Sage200Microservice.Data", "1.0.0");
    }
}