using System;
using System.Collections.Generic;
using Sage200Microservice.Services.Models; // for SageAllocationHistoryItem

namespace Sage200Microservice.Services.Models.Customers
{
    public sealed class CustomerDetails
    {
        public string CustomerReference { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Telephone { get; set; }

        public List<CustomerAddress> Addresses { get; set; } = new();

        public PagedResult<InvoiceSummary> RecentInvoices { get; set; } = new();

        public int OpenItemsCount { get; set; }
        public decimal OutstandingBalance { get; set; }

        public List<AllocationSummary> RecentAllocations { get; set; } = new();
    }

    public sealed class CustomerAddress
    {
        public string Line1 { get; set; } = "";
        public string? Line2 { get; set; }
        public string? City { get; set; }
        public string? Postcode { get; set; }
        public string? Country { get; set; }
        public string Type { get; set; } = "Primary";
    }

    public sealed class InvoiceSummary
    {
        public string DocumentNo { get; set; } = "";
        public DateTime? OrderDateUtc { get; set; }
        public decimal GrossValue { get; set; }
        public decimal OutstandingValue { get; set; }
        public bool IsPaid => OutstandingValue == 0;
        public List<SageAllocationHistoryItem> Allocations { get; set; } = new();
    }

    public sealed class AllocationSummary
    {
        public string DocumentNo { get; set; } = "";
        public string TraderTransactionType { get; set; } = "";
        public DateTime? AllocationDateUtc { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int? TotalCount { get; set; }
        public List<T> Items { get; set; } = new();
    }
}
