namespace Sage200Microservice.API.DTOs
{
    /// <summary>
    /// DTO for creating a customer
    /// </summary>
    public class CreateCustomerRequestDto
    {
        /// <summary>
        /// The name of the customer
        /// </summary>
        /// <example> Acme Corporation </example>
        public string CustomerName { get; set; }

        /// <summary>
        /// The unique code for the customer
        /// </summary>
        /// <example> ACME001 </example>
        public string CustomerCode { get; set; }

        /// <summary>
        /// The first line of the customer's address
        /// </summary>
        /// <example> 123 Main Street </example>
        public string AddressLine1 { get; set; }

        /// <summary>
        /// The second line of the customer's address
        /// </summary>
        /// <example> Suite 100 </example>
        public string AddressLine2 { get; set; }

        /// <summary>
        /// The city of the customer's address
        /// </summary>
        /// <example> London </example>
        public string City { get; set; }

        /// <summary>
        /// The postcode of the customer's address
        /// </summary>
        /// <example> EC1A 1BB </example>
        public string Postcode { get; set; }

        /// <summary>
        /// The telephone number of the customer
        /// </summary>
        /// <example> +44 20 1234 5678 </example>
        public string Telephone { get; set; }

        /// <summary>
        /// The email address of the customer
        /// </summary>
        /// <example> contact@acme.com </example>
        public string Email { get; set; }
    }

    /// <summary>
    /// DTO for customer creation result
    /// </summary>
    public class CreateCustomerResultDto
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
        /// The ID of the created customer
        /// </summary>
        public long CustomerId { get; set; }

        /// <summary>
        /// The unique code of the created customer
        /// </summary>
        public string CustomerCode { get; set; }
    }

    /// <summary>
    /// DTO for Sage customer result
    /// </summary>
    public class SageCustomerResultDto
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
        /// The customer details
        /// </summary>
        public SageCustomerDto Customer { get; set; }
    }

    /// <summary>
    /// DTO for Sage customer
    /// </summary>
    public class SageCustomerDto
    {
        /// <summary>
        /// The customer ID in Sage
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// The customer reference
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// The customer name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The customer's address
        /// </summary>
        public SageAddressDto Address { get; set; }

        /// <summary>
        /// The customer's contact details
        /// </summary>
        public SageContactDto Contact { get; set; }

        /// <summary>
        /// The customer's tax information
        /// </summary>
        public SageTaxInfoDto TaxInfo { get; set; }

        /// <summary>
        /// The customer's credit information
        /// </summary>
        public SageCreditInfoDto CreditInfo { get; set; }
    }

    /// <summary>
    /// DTO for Sage address
    /// </summary>
    public class SageAddressDto
    {
        /// <summary>
        /// The first line of the address
        /// </summary>
        public string Line1 { get; set; }

        /// <summary>
        /// The second line of the address
        /// </summary>
        public string Line2 { get; set; }

        /// <summary>
        /// The city
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// The postcode
        /// </summary>
        public string Postcode { get; set; }

        /// <summary>
        /// The country
        /// </summary>
        public string Country { get; set; }
    }

    /// <summary>
    /// DTO for Sage contact
    /// </summary>
    public class SageContactDto
    {
        /// <summary>
        /// The telephone number
        /// </summary>
        public string Telephone { get; set; }

        /// <summary>
        /// The email address
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// The website
        /// </summary>
        public string Website { get; set; }
    }

    /// <summary>
    /// DTO for Sage tax information
    /// </summary>
    public class SageTaxInfoDto
    {
        /// <summary>
        /// The tax number
        /// </summary>
        public string TaxNumber { get; set; }

        /// <summary>
        /// The tax code
        /// </summary>
        public string TaxCode { get; set; }
    }

    /// <summary>
    /// DTO for Sage credit information
    /// </summary>
    public class SageCreditInfoDto
    {
        /// <summary>
        /// The credit limit
        /// </summary>
        public decimal CreditLimit { get; set; }

        /// <summary>
        /// The credit status
        /// </summary>
        public string CreditStatus { get; set; }
    }

    /// <summary>
    /// DTO for customer filter request
    /// </summary>
    public class CustomerFilterRequestDto
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
        /// <example> CustomerName </example>
        public string SortBy { get; set; } = "CustomerName";

        /// <summary>
        /// The sort direction (asc or desc)
        /// </summary>
        /// <example> asc </example>
        public string SortDirection { get; set; } = "asc";

        /// <summary>
        /// Filter by customer code
        /// </summary>
        /// <example> ACME001 </example>
        public string CustomerCode { get; set; }

        /// <summary>
        /// Filter by customer name
        /// </summary>
        /// <example> Acme </example>
        public string CustomerName { get; set; }

        /// <summary>
        /// Filter by city
        /// </summary>
        /// <example> London </example>
        public string City { get; set; }

        /// <summary>
        /// Filter by postcode
        /// </summary>
        /// <example> EC1A </example>
        public string Postcode { get; set; }

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
    /// DTO for customer response
    /// </summary>
    public class CustomerResponseDto
    {
        /// <summary>
        /// The customer ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The customer name
        /// </summary>
        public string CustomerName { get; set; }

        /// <summary>
        /// The customer code
        /// </summary>
        public string CustomerCode { get; set; }

        /// <summary>
        /// The first line of the address
        /// </summary>
        public string AddressLine1 { get; set; }

        /// <summary>
        /// The second line of the address
        /// </summary>
        public string AddressLine2 { get; set; }

        /// <summary>
        /// The city
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// The postcode
        /// </summary>
        public string Postcode { get; set; }

        /// <summary>
        /// The telephone number
        /// </summary>
        public string Telephone { get; set; }

        /// <summary>
        /// The email address
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// The date and time when the customer was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The user who created the customer
        /// </summary>
        public string CreatedBy { get; set; }
    }
}