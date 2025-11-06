using Prometheus;

namespace Sage200Microservice.API.Metrics
{
    /// <summary>
    /// Metrics for database operations
    /// </summary>
    public class DatabaseMetrics
    {
        /// <summary>
        /// Counter for database queries
        /// </summary>
        public readonly Counter DatabaseQueries;

        /// <summary>
        /// Counter for database errors
        /// </summary>
        public readonly Counter DatabaseErrors;

        /// <summary>
        /// Histogram for database query duration
        /// </summary>
        public readonly Histogram DatabaseQueryDuration;

        /// <summary>
        /// Gauge for active database connections
        /// </summary>
        public readonly Gauge ActiveDatabaseConnections;

        /// <summary>
        /// Initializes a new instance of the DatabaseMetrics class
        /// </summary>
        public DatabaseMetrics()
        {
            // Create metrics with appropriate labels
            DatabaseQueries = Prometheus.Metrics.CreateCounter(
                "sage200_database_queries_total",
                "Total number of database queries",
                new CounterConfiguration
                {
                    LabelNames = new[] { "operation", "entity" }
                });

            DatabaseErrors = Prometheus.Metrics.CreateCounter(
                "sage200_database_errors_total",
                "Total number of database errors",
                new CounterConfiguration
                {
                    LabelNames = new[] { "operation", "entity", "error_type" }
                });

            DatabaseQueryDuration = Prometheus.Metrics.CreateHistogram(
                "sage200_database_query_duration_seconds",
                "Database query duration in seconds",
                new HistogramConfiguration
                {
                    LabelNames = new[] { "operation", "entity" },
                    Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) // 1ms to ~1s
                });

            ActiveDatabaseConnections = Prometheus.Metrics.CreateGauge(
                "sage200_database_active_connections",
                "Number of currently active database connections",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "database" }
                });
        }

        /// <summary>
        /// Records a database query
        /// </summary>
        /// <param name="operation"> The database operation </param>
        /// <param name="entity">    The entity being operated on </param>
        public void RecordDatabaseQuery(string operation, string entity)
        {
            DatabaseQueries.WithLabels(operation, entity).Inc();
        }

        /// <summary>
        /// Records a database error
        /// </summary>
        /// <param name="operation"> The database operation </param>
        /// <param name="entity">    The entity being operated on </param>
        /// <param name="errorType"> The error type </param>
        public void RecordDatabaseError(string operation, string entity, string errorType)
        {
            DatabaseErrors.WithLabels(operation, entity, errorType).Inc();
        }

        /// <summary>
        /// Records a database query duration
        /// </summary>
        /// <param name="operation"> The database operation </param>
        /// <param name="entity">    The entity being operated on </param>
        /// <param name="duration">  The duration in seconds </param>
        public void RecordDatabaseQueryDuration(string operation, string entity, double duration)
        {
            DatabaseQueryDuration.WithLabels(operation, entity).Observe(duration);
        }

        /// <summary>
        /// Tracks a database query
        /// </summary>
        /// <param name="operation"> The database operation </param>
        /// <param name="entity">    The entity being operated on </param>
        /// <returns> A timer that records the duration when disposed </returns>
        public IDisposable TrackDatabaseQuery(string operation, string entity)
        {
            return DatabaseQueryDuration.WithLabels(operation, entity).NewTimer();
        }

        /// <summary>
        /// Tracks a database connection
        /// </summary>
        /// <param name="database"> The database name </param>
        /// <returns> A disposable that decrements the gauge when disposed </returns>
        public IDisposable TrackDatabaseConnection(string database)
        {
            return ActiveDatabaseConnections.WithLabels(database).TrackInProgress();
        }
    }
}