namespace Sage200Microservice.Services.Models
{
    public class SageSalesOrder
    {
        public long id { get; set; }

        // Some tenants call this document_no; some just reference
        public string? document_no { get; set; }
        public string? reference { get; set; }

        public long customer_id { get; set; }

        // Some payloads have order_date, others only posted_date
        public DateTime? order_date { get; set; }
        public DateTime? posted_date { get; set; }

        // Either document_* or plain fields
        public decimal? document_gross_value { get; set; }
        public decimal? document_outstanding_value { get; set; }
        public decimal? gross_value { get; set; }
        public decimal? outstanding_value { get; set; }

        public string? trader_transaction_type { get; set; }

        public List<SageOrderLine>? lines { get; set; }
        public List<SageAllocationHistoryItem>? allocation_history_items { get; set; }
    }

    public class SageOrderLine
    {
        public string? product_code { get; set; }
        public decimal? quantity { get; set; }
        public decimal? unit_price { get; set; }
    }

    public class SageAllocationHistoryItem
    {
        public string? allocation_reference { get; set; }
        public decimal? allocated_value { get; set; }
        public DateTime? allocation_date { get; set; }
        public string? trader_transaction_type { get; set; }
    }
}
