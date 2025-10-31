using System;
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.Data.Models
{
    public class InvoiceStatusHistory
    {
        public long Id { get; set; }
        public string InvoiceReference { get; set; } = "";
        public decimal GrossValue { get; set; }
        public decimal OutstandingValue { get; set; }
        public decimal AllocatedValue { get; set; }
        public string Status { get; set; } = "";
        public DateTime CheckTimestamp { get; set; }
        public string Source { get; set; } = "";
        public string CheckedBy { get; set; } = "";
        [Required]
        public string CorrelationId { get; set; } = "";
    }
}