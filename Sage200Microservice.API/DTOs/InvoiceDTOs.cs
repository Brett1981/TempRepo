namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for creating a sales order invoice
    /// </summary>
    public class CreateSalesOrderInvoiceRequestDto
    {
        /// <summary>
        /// The ID of the customer
        /// </summary>
        /// <example> 12345 </example>
        public int CustomerId { get; set; }

        /// <summary>
        /// The list of order lines
        /// </summary>
        public List<OrderLineRequestDto> Lines { get; set; }
    }

    /// <summary>
    /// DTO for an order line request
    /// </summary>
    public class OrderLineRequestDto
    {
        /// <summary>
        /// The product code
        /// </summary>
        /// <example> PROD001 </example>
        public string ProductCode { get; set; }

        /// <summary>
        /// The quantity of the product
        /// </summary>
        /// <example> 5.0 </example>
        public decimal Quantity { get; set; }

        /// <summary>
        /// The unit price of the product
        /// </summary>
        /// <example> 19.99 </example>
        public decimal UnitPrice { get; set; }
    }

    /// <summary>
    /// DTO for sales order invoice creation result
    /// </summary>
    public class CreateSalesOrderInvoiceResultDto
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The ID of the created order
        /// </summary>
        public long OrderId { get; set; }

        /// <summary>
        /// The reference of the created order
        /// </summary>
        public string OrderReference { get; set; }
    }

    /// <summary>
    /// DTO for invoice status result
    /// </summary>
    public class InvoiceStatusResultDto
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The invoice reference
        /// </summary>
        public string InvoiceReference { get; set; }

        /// <summary>
        /// Indicates whether the invoice is fully paid
        /// </summary>
        public bool IsPaid { get; set; }

        /// <summary>
        /// Indicates whether the invoice has been credited
        /// </summary>
        public bool IsCredited { get; set; }

        /// <summary>
        /// The outstanding value of the invoice
        /// </summary>
        public decimal OutstandingValue { get; set; }

        /// <summary>
        /// The allocated value of the invoice
        /// </summary>
        public decimal AllocatedValue { get; set; }

        /// <summary>
        /// The gross value of the invoice
        /// </summary>
        public decimal GrossValue { get; set; }

        /// <summary>
        /// The allocation history of the invoice
        /// </summary>
        public List<SageAllocationHistoryItemDto> AllocationHistory { get; set; }
    }

    /// <summary>
    /// DTO for Sage allocation history item
    /// </summary>
    public class SageAllocationHistoryItemDto
    {
        /// <summary>
        /// The allocation reference
        /// </summary>
        public string AllocationReference { get; set; }

        /// <summary>
        /// The allocated value
        /// </summary>
        public decimal AllocatedValue { get; set; }

        /// <summary>
        /// The allocation date
        /// </summary>
        public DateTime AllocationDate { get; set; }

        /// <summary>
        /// The trader transaction type
        /// </summary>
        public string TraderTransactionType { get; set; }
    }

    /// <summary>
    /// DTO for process result
    /// </summary>
    public class ProcessResultDto
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result of the operation
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// DTO for invoice filter request
    /// </summary>
    public class InvoiceFilterRequestDto
    {
        /// <summary>
        /// The page number (1-based)
        /// </summary>
        /// <example> 1 </example>
        public int Page { get; set; } = 1;

        /// <summary>
        /// The number of items per page
        /// </summary>
        /// <example> 10 </example>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// The sort field
        /// </summary>
        /// <example> CreatedAt </example>
        public string SortBy { get; set; } = "CreatedAt";

        /// <summary>
        /// The sort direction (asc or desc)
        /// </summary>
        /// <example> desc </example>
        public string SortDirection { get; set; } = "desc";

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

        /// <summary>
        /// Filter by creation date (start)
        /// </summary>
        /// <example> 2023-01-01 </example>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// Filter by creation date (end)
        /// </summary>
        /// <example> 2023-12-31 </example>
        public DateTime? EndDate { get; set; }
    }

    /// <summary>
    /// DTO for invoice response
    /// </summary>
    public class InvoiceResponseDto
    {
        /// <summary>
        /// The invoice ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The customer ID
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// The invoice reference
        /// </summary>
        public string InvoiceReference { get; set; }

        /// <summary>
        /// The Sage ID
        /// </summary>
        public long? SageId { get; set; }

        /// <summary>
        /// The gross value of the invoice
        /// </summary>
        public decimal GrossValue { get; set; }

        /// <summary>
        /// The outstanding value of the invoice
        /// </summary>
        public decimal OutstandingValue { get; set; }

        /// <summary>
        /// The status of the invoice
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Indicates whether the invoice is synced with Sage
        /// </summary>
        public bool IsSynced { get; set; }

        /// <summary>
        /// The date and time when the invoice was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The user who created the invoice
        /// </summary>
        public string CreatedBy { get; set; }

        /// <summary>
        /// The date and time when the invoice was last checked
        /// </summary>
        public DateTime LastCheckedAt { get; set; }
    }
}