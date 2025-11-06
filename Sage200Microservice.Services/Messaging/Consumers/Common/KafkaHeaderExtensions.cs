using System;
using System.Linq;
using System.Text;
using Confluent.Kafka;

namespace Sage200Microservice.Services.Messaging.Consumers.Common
{
    /// <summary>
    /// Helpers to read Kafka headers with case-insensitive keys and safe UTF8 decoding.
    /// </summary>
    public static class KafkaHeaderExtensions
    {
        /// <summary>
        /// Returns the last value for a header key (case-insensitive), decoded as UTF8, or null if not present/decoding fails.
        /// </summary>
        public static string? TryGetLastValue(this Headers headers, string key)
        {
            if (headers is null || string.IsNullOrWhiteSpace(key)) return null;

            // Confluent's Headers is case-sensitive; emulate case-insensitive lookup
            var matches = headers.Where(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
            var last = matches.LastOrDefault();
            if (last is null) return null;

            try
            {
                var bytes = last.GetValueBytes();
                return bytes is null ? null : Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
