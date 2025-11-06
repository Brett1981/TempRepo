using System;

namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Controls background OAuth token maintenance.
    /// </summary>
    public sealed class TokenMaintenanceOptions
    {
        /// <summary>Enable/disable the background maintenance loop.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How often to check token state.</summary>
        public int CheckPeriodSeconds { get; set; } = 60;

        /// <summary>
        /// Proactively refresh when the access token has less than this many seconds left.
        /// Recommended: 600 (10 minutes).
        /// </summary>
        public int ProactiveRefreshSeconds { get; set; } = 600;

        /// <summary>Initial delay on startup (seconds) to let app warm up.</summary>
        public int StartupDelaySeconds { get; set; } = 3;

        /// <summary>Random jitter added/subtracted to avoid thundering herd.</summary>
        public int JitterSeconds { get; set; } = 15;
    }
}
