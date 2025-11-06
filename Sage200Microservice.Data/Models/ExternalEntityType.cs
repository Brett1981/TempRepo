using System.ComponentModel;

namespace Sage200Microservice.Data.Models
{
    /// <summary>
    /// Closed set of linkable Sage entities. Persisted as NVARCHAR(40) via string conversion.
    /// </summary>
    public enum ExternalEntityType
    {
        /// <summary>Customer entity (Sage Customers).</summary>
        Customer = 0,

        /// <summary>Sales Order Processing Order (SOP Order).</summary>
        SopOrder = 1,

        /// <summary>Sales Receipt entity.</summary>
        SalesReceipt = 2,

        /// <summary>Sales Payment entity.</summary>
        SalesPayment = 3,

        /// <summary>Sales Credit Note entity.</summary>
        SalesCreditNote = 4,

        /// <summary>Sales Invoice entity.</summary>
        SalesInvoice = 5
    }
}