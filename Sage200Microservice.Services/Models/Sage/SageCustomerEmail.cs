using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// [NEW ADDITION - STAGE 10]
    /// Represents the 'customer_email' schema from sales.json.
    /// Used within SageCustomerContact.
    /// </summary>
    public class SageCustomerEmail
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } // ReadOnly

        [MaxLength(227)]
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("customer_contact_id")]
        public long? CustomerContactId { get; set; } // ReadOnly

        [JsonPropertyName("is_default")]
        public bool? IsDefault { get; set; }

        [JsonPropertyName("is_to_delete")]
        public bool? IsToDelete { get; set; } // For PUT/DELETE

        [JsonPropertyName("date_time_created")]
        public DateTimeOffset? DateTimeCreated { get; set; } // ReadOnly

        [JsonPropertyName("date_time_updated")]
        public DateTimeOffset? DateTimeUpdated { get; set; } // ReadOnly

        // 'customer_contact' navigation property omitted
    }
}
