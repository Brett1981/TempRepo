using System;
using System.Collections.Generic;

namespace Sage200Microservice.API.Models.Customers
{
    public sealed class CustomerDetailsDto
    {
        public string CustomerReference { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Telephone { get; set; } = "";

        public List<CustomerAddressDto> Addresses { get; set; } = new();

        public PagedResultDto<InvoiceSummaryDto> RecentInvoices { get; set; } = new();

        public int OpenItemsCount { get; set; }
        public decimal OutstandingBalance { get; set; }

        public List<AllocationSummaryDto> RecentAllocations { get; set; } = new();
    }

    public sealed class CustomerAddressDto
    {
        public string? Line1 { get; set; }
        public string? Line2 { get; set; }
        public string? City { get; set; }
        public string? Postcode { get; set; }
        public string? Country { get; set; }
        public string Type { get; set; } = "Primary";
    }

    public sealed class InvoiceSummaryDto
    {
        public string DocumentNo { get; set; } = "";
        public DateTime? OrderDateUtc { get; set; }
        public decimal GrossValue { get; set; }
        public decimal OutstandingValue { get; set; }
        public bool IsPaid { get; set; }
        public List<AllocationSummaryDto> Allocations { get; set; } = new();
    }

    public sealed class AllocationSummaryDto
    {
        public string DocumentNo { get; set; } = "";
        public string TraderTransactionType { get; set; } = "";
        public DateTime? AllocationDateUtc { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class PagedResultDto<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int? TotalCount { get; set; }
        public List<T> Items { get; set; } = new();
    }
}
