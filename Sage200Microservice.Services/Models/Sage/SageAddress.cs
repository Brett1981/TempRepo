using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// [UPDATED - STAGE 10 - CORRECTED]
    /// Represents the main_address structure in Sage OpenAPI definitions.
    /// Used within SageCustomer. Based on sales.json#/definitions/customer/properties/main_address.
    /// Includes Country field.
    /// </summary>
    public class SageAddress
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } // ReadOnly

        [JsonPropertyName("customer_id")]
        public long? CustomerId { get; set; } // ReadOnly

        [MaxLength(60)]
        [JsonPropertyName("address_1")]
        public string? Address1 { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("address_2")]
        public string? Address2 { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("address_3")]
        public string? Address3 { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("address_4")]
        public string? Address4 { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("city")]
        public string? City { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("county")]
        public string? County { get; set; }

        [MaxLength(10)]
        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("country")]
        public string? Country { get; set; } // ReadOnly according to schema

        // Address Country Code ID allows setting the country
        [JsonPropertyName("address_country_code_id")]
        public long? AddressCountryCodeId { get; set; }

        [JsonPropertyName("date_time_created")]
        public DateTimeOffset? DateTimeCreated { get; set; } // ReadOnly

        [JsonPropertyName("date_time_updated")]
        public DateTimeOffset? DateTimeUpdated { get; set; } // ReadOnly

        // Navigation properties (address_country_code, contact_name) omitted
    }
}

