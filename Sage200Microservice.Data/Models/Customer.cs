namespace Sage200Microservice.Data.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string CustomerCode { get; set; }
        public string AddressLine1 { get; set; }
        public string AddressLine2 { get; set; }
        public string City { get; set; }
        public string Postcode { get; set; }
        public string Telephone { get; set; }
        public string Email { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }

        // New fields for Sage 200 integration
        public long? SageId { get; set; }

        public bool IsSynced { get; set; } = true;
        public DateTime? LastSyncedAt { get; set; }
    }
}