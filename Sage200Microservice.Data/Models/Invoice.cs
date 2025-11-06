using System.ComponentModel.DataAnnotations.Schema;

namespace Sage200Microservice.Data.Models
{
    public class Invoice
    {
        public long Id { get; set; }
        public string InvoiceReference { get; set; } = null!;
        public int CustomerId { get; set; }
        public Customer Customer { get; set; } = null!;  // <- add = null! to avoid CS8618 warnings
        public decimal GrossValue { get; set; }
        public decimal OutstandingValue { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime LastCheckedAt { get; set; }   // make this DateTime? if it should be optional
        public string CreatedBy { get; set; } = null!;
        public long? SageId { get; set; }
        public bool IsSynced { get; set; } = true;    // add .HasDefaultValue(true) in the mapping if you want a DB default
        public DateTime? LastSyncedAt { get; set; }
        [NotMapped] public decimal TotalAmount => GrossValue;
    }
}