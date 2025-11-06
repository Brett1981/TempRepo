namespace Sage200Microservice.API.Models
{
    /// <summary>
    /// Base class for filter requests
    /// </summary>
    public class FilterRequest : PaginationRequest
    {
        /// <summary>
        /// The search term for text-based filtering
        /// </summary>
        /// <example> acme </example>
        public string SearchTerm { get; set; }

        /// <summary>
        /// The start date for date range filtering
        /// </summary>
        /// <example> 2023-01-01 </example>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The end date for date range filtering
        /// </summary>
        /// <example> 2023-12-31 </example>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// The status filter
        /// </summary>
        /// <example> active </example>
        public string Status { get; set; }
    }

    /// <summary>
    /// Filter request for API keys
    /// </summary>
    public class ApiKeyFilterRequest : FilterRequest
    {
        /// <summary>
        /// Filter by active status
        /// </summary>
        /// <example> true </example>
        public bool? IsActive { get; set; }

        /// <summary>
        /// Filter by expiration status
        /// </summary>
        /// <example> true </example>
        public bool? IsExpired { get; set; }

        /// <summary>
        /// Filter by client name
        /// </summary>
        /// <example> Mobile App </example>
        public string ClientName { get; set; }
    }

    /// <summary>
    /// Filter request for invoices
    /// </summary>
    public class InvoiceFilterRequest : FilterRequest
    {
        /// <summary>
        /// Filter by customer ID
        /// </summary>
        /// <example> 12345 </example>
        public int? CustomerId { get; set; }

        /// <summary>
        /// Filter by payment status
        /// </summary>
        /// <example> Paid </example>
        public string PaymentStatus { get; set; }

        /// <summary>
        /// Filter by minimum amount
        /// </summary>
        /// <example> 100.00 </example>
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// Filter by maximum amount
        /// </summary>
        /// <example> 1000.00 </example>
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// Filter by invoice reference
        /// </summary>
        /// <example> INV-2023-001 </example>
        public string InvoiceReference { get; set; }
    }

    /// <summary>
    /// Filter request for customers
    /// </summary>
    public class CustomerFilterRequest : FilterRequest
    {
        /// <summary>
        /// Filter by customer code
        /// </summary>
        /// <example> ACME001 </example>
        public string CustomerCode { get; set; }

        /// <summary>
        /// Filter by city
        /// </summary>
        /// <example> London </example>
        public string City { get; set; }

        /// <summary>
        /// Filter by postcode
        /// </summary>
        /// <example> EC1A 1BB </example>
        public string Postcode { get; set; }
    }
}