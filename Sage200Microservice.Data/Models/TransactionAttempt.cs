using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sage200Microservice.Data.Models
{
    public class TransactionAttempt
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string CorrelationId { get; set; } = string.Empty;

        [Required]
        public DateTime ReceivedTimestamp { get; set; }

        [Required]
        [MaxLength(50)]
        public string SourceSystem { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string TriggeringEventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string EntityType { get; set; } = string.Empty;

        public byte[]? Payload { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProcessingStatus { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? KafkaTopic { get; set; }

        public int? KafkaPartition { get; set; }

        public long? KafkaOffset { get; set; }

        [MaxLength(200)]
        public string? KafkaMessageKey { get; set; }

        [MaxLength(64)]
        public string? SiteId { get; set; }

        [MaxLength(32)]
        public string? CompanyId { get; set; }

        [MaxLength(88)]
        public string? IdempotencyKeyHash { get; set; }

        [MaxLength(200)]
        public string? ExternalRef { get; set; }

        public int? ApiKeyId { get; set; } // Foreign key property

        public DateTime? ProcessingStartedUtc { get; set; }

        public DateTime? ProcessingCompletedUtc { get; set; }

        public int? DurationMs { get; set; }

        [Required]
        public int AttemptNumber { get; set; }

        [Required]
        public int RetryCount { get; set; }

        [MaxLength(128)]
        public string? SageUrn { get; set; }

        public long? SageId { get; set; }

        [MaxLength(50)]
        public string? ResultCode { get; set; }

        [MaxLength(1024)]
        public string? ResultMessage { get; set; }

        [MaxLength(4000)]
        public string? OriginalHeadersJson { get; set; }
    }
}