using System.ComponentModel.DataAnnotations;


namespace Sage200Microservice.API.DTOs
{
    public sealed class ReplayRequestDto
    {
        [Required]
        public string CorrelationId { get; set; } = string.Empty;


        [Required]
        public string OriginalTopic { get; set; } = string.Empty;


        [Required]
        public string DlqPayloadJson { get; set; } = string.Empty; // Serialized DlqEnvelope or original payload


        public string? TargetTopicOverride { get; set; } // if provided, replays to this topic instead of OriginalTopic
    }
}