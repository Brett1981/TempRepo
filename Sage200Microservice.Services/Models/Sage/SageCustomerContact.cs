using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sage200Microservice.Services.Models.Sage
{
    /// <summary>
    /// [NEW ADDITION - STAGE 10 - CORRECTED]
    /// Represents the 'customer_contact' schema from sales.json.
    /// Used within SageCustomer.
    /// </summary>
    public class SageCustomerContact
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } // ReadOnly

        [JsonPropertyName("customer_id")]
        public long? CustomerId { get; set; } // ReadOnly

        [JsonPropertyName("salutation_id")]
        public long? SalutationId { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("middle_name")]
        public string? MiddleName { get; set; }

        [MaxLength(60)]
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("name")]
        [MaxLength(180)]
        public string? Name { get; set; } // ReadOnly

        [JsonPropertyName("default_telephone")]
        [MaxLength(227)]
        public string? DefaultTelephone { get; set; } // ReadOnly

        [JsonPropertyName("default_email")]
        [MaxLength(227)]
        public string? DefaultEmail { get; set; } // ReadOnly

        [JsonPropertyName("is_default")]
        public bool? IsDefault { get; set; } // ReadOnly according to schema, but might be needed for setting primary contact? Keeping nullable bool.

        [JsonPropertyName("is_to_delete")]
        public bool? IsToDelete { get; set; } // For PUT/DELETE

        [JsonPropertyName("emails")]
        public List<SageCustomerEmail>? Emails { get; set; }

        // TODO: Add Telephones, Mobiles, Faxes, Websites, Roles later if needed
        // public List<SageCustomerTelephone>? Telephones { get; set; }
        // public List<SageCustomerMobile>? Mobiles { get; set; }
        // public List<SageCustomerFax>? Faxes { get; set; }
        // public List<SageCustomerWebsite>? Websites { get; set; }
        // public List<SageCustomerContactRole>? Roles { get; set; }

        [JsonPropertyName("date_time_created")]
        public DateTimeOffset? DateTimeCreated { get; set; } // ReadOnly

        [JsonPropertyName("date_time_updated")]
        public DateTimeOffset? DateTimeUpdated { get; set; } // ReadOnly
    }
}

