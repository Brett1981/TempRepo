using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sage200Microservice.Services.Models.Sales
{
    public sealed class SalesCreateResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// URN returned by Sage.
        /// </summary>
        public string? Urn { get; set; }

        /// <summary>
        /// Typed failure (when Success == false) so controllers can map status codes.
        /// </summary>
        public FailureKind Failure { get; set; } = FailureKind.None;

        /// <summary>
        /// Optional upstream HTTP status (when Failure == Upstream).
        /// </summary>
        public int? UpstreamStatusCode { get; set; }

        /// <summary>
        /// Optional upstream body excerpt for diagnostics.
        /// </summary>
        public string? UpstreamBody { get; set; }
    }
}
