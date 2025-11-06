using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// Represents the 'delivery_address' schema within sop_order.
    /// Based on sop.json definitions.
    /// </summary>
    public class SageDeliveryAddress
    {
        // ReadOnly properties (id, date_time_created, date_time_updated, nested objects) omitted for create/update

        [MaxLength(60)]
        [JsonPropertyName("postal_name")]
        public string? PostalName { get; set; }

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

        [MaxLength(60)]
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [MaxLength(10)]
        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }

        [MaxLength(235)]
        [JsonPropertyName("contact")]
        public string? Contact { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("telephone_number")]
        public string? TelephoneNumber { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("fax_number")]
        public string? FaxNumber { get; set; }

        [MaxLength(255)]
        [JsonPropertyName("email_address")]
        public string? EmailAddress { get; set; }

        [MaxLength(30)]
        [JsonPropertyName("tax_number")]
        public string? TaxNumber { get; set; }

        [JsonPropertyName("tax_code_id")]
        public long? TaxCodeId { get; set; }

        [JsonPropertyName("country_code_id")]
        public long? CountryCodeId { get; set; } // VAT details country code Id
    }
}
