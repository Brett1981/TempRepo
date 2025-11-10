using System.Diagnostics.Metrics;


namespace Sage200Microservice.Data.Telemetry
{
    /// <summary>
    /// Central place for OpenTelemetry metrics used by retry/DLQ flows.
    /// </summary>
    public static class Metrics
    {
        private static readonly Meter Meter = new("sage200microservice", "1.0.0");


        public static readonly Counter<long> RetriesTotal = Meter.CreateCounter<long>(
        name: "microservice_retries_total",
        unit: "attempts",
        description: "Total number of retry attempts across consumers.");


        public static readonly Counter<long> DlqMessagesTotal = Meter.CreateCounter<long>(
        name: "microservice_dlq_messages_total",
        unit: "messages",
        description: "Total number of messages sent to DLQ.");


        public static readonly Counter<long> MessageFailuresTotal = Meter.CreateCounter<long>(
        name: "microservice_message_failures_total",
        unit: "messages",
        description: "Total number of failed message processing attempts.");


        public static readonly Counter<long> ReplayRequestsTotal = Meter.CreateCounter<long>(
        name: "microservice_replay_requests_total",
        unit: "requests",
        description: "Total number of manual replay requests received.");
    }
}