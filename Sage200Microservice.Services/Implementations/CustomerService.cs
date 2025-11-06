

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sage200Microservice.API.DTOs;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Implementations.Sales;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Messaging;
using Sage200Microservice.Services.Messaging.Requests;
using Sage200Microservice.Services.Models;
using Sage200Microservice.Services.Models.Customers;
using Sage200Microservice.Services.Models.Sage;
using Sage200Microservice.Services.Models.Sop;
using Sage200Microservice.Services.Shared;
using Sage200Microservice.Services.Tracing;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Net.WebRequestMethods;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Customer service that talks to Sage 200 and the local DB.
    /// Refactored to use strongly-typed Sage DTOs (Services.Models.Sage)
    /// and System.Text.Json serialization instead of manual dictionary building.
    /// </summary>
    public class CustomerService : ICustomerService
    {
        private readonly ILogger<CustomerService> _logger;
        private readonly ICustomerRepository _customerRepository;
        private readonly ISageApiClient _sageApiClient;
        private readonly IEventPublisher? _events;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ISopOrderService _sopOrders;
        private readonly ISalesInvoicesService _salesInvoices;

        public CustomerService(
            ILogger<CustomerService> logger,
            ICustomerRepository customerRepository,
            ISageApiClient sageApiClient,
            IOptions<JsonSerializerOptions> jsonOptions,
            ISopOrderService sopOrders,
            ISalesInvoicesService salesInvoices,
            IEventPublisher? events = null)
        {
            _logger = logger;
            _customerRepository = customerRepository;
            _sageApiClient = sageApiClient;
            _sopOrders = sopOrders;
            _salesInvoices = salesInvoices;
            _events = events;

            // Configure options specifically for Sage interaction
            _jsonOptions = new JsonSerializerOptions(jsonOptions?.Value ?? new JsonSerializerOptions())
            {
                // Use SnakeCaseLower OR rely on [JsonPropertyName]
                // Using attributes is safer
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true // For deserialization
            };
        }

        /// <summary>
        ///Legacy signature. Matches ICustomerService. Delegates to the preferred overload.
        /// </summary>
        public Task<(bool Success, string Message, long CustomerId, string CustomerCode)>
            CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Legacy CreateCustomerAsync(Customer) overload invoked for {CustomerCode}", customer.CustomerCode);
            // Call the preferred overload using a default context
            return CreateCustomerAsync(customer, new DefaultHttpContext(), cancellationToken);
        }

        /// <summary>
        ///Preferred signature: Accepts Data.Models.Customer. Matches ICustomerService.
        /// Maps to SageCustomer DTO, serializes, calls Sage API, parses response, persists locally.
        /// </summary>
        public async Task<(bool Success, string Message, long CustomerId, string CustomerCode)>
            CreateCustomerAsync(Customer customerDataModel, HttpContext http, CancellationToken cancellationToken = default)
        {
            if (customerDataModel == null) throw new ArgumentNullException(nameof(customerDataModel));
            if (http == null) throw new ArgumentNullException(nameof(http));

            // Ensure local model properties are set
            customerDataModel.CreatedAt = DateTime.UtcNow;
            customerDataModel.CreatedBy = http.Request.Headers["caller-id"].FirstOrDefault() ?? "API";
            customerDataModel.IsSynced = false; // Start as not synced

            long finalLocalId = 0;

            try
            {
                using var activity = TracingHelper.CreateChildActivity("CustomerService.CreateCustomer");
                activity?.SetTag("customer.code", customerDataModel.CustomerCode);

                // --- Map local model to Sage DTO ---
                var sageCustomerDto = MapToSageCustomerCreate(customerDataModel);

                // --- Serialize Sage DTO ---
                string payloadJson = JsonSerializer.Serialize(sageCustomerDto, _jsonOptions);
                _logger.LogTrace("Sage Customer Payload: {Payload}", payloadJson);

                // Prepare headers
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (http.Request.Headers.TryGetValue("X-Site", out var xSite) && !StringValues.IsNullOrEmpty(xSite)) headers["X-Site"] = xSite.ToString();
                if (http.Request.Headers.TryGetValue("X-Company", out var xCompany) && !StringValues.IsNullOrEmpty(xCompany)) headers["X-Company"] = xCompany.ToString();
                if (http.Request.Headers.TryGetValue("Idempotency-Key", out var idem) && !StringValues.IsNullOrEmpty(idem))
                    headers["Idempotency-Key"] = idem.ToString();
                else
                    headers["Idempotency-Key"] = HashBase64Url(payloadJson) ?? Guid.NewGuid().ToString("N");

                // Call Sage API
                long? sageId = null;
                try
                {
                    using var apiCallActivity = TracingHelper.CreateChildActivity("SageApi.CreateCustomer");
                    apiCallActivity?.SetTag("sage.customer_code", customerDataModel.CustomerCode);

                    // Using PostForBodyAsync to handle non-success responses gracefully
                    var (status, bodyText) = await _sageApiClient.PostForBodyAsync("customers", payloadJson, headers, cancellationToken);

                    if (status < 200 || status > 299)
                    {
                        _logger.LogWarning("Upstream error creating customer: status={Status}, body={BodyPreview}", status, SafePreview(bodyText));
                        throw new HttpRequestException($"Upstream error ({status}) creating customer.", null, (HttpStatusCode)status);
                    }

                    // Deserialize Response to SageCustomer DTO
                    try
                    {
                        // Use case-insensitive options for robust deserialization
                        var createdSageCustomer = JsonSerializer.Deserialize<SageCustomer>(bodyText, _jsonOptions);
                        sageId = createdSageCustomer?.Id;
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to deserialize Sage customer creation response. Status: {Status}, Body: {BodyPreview}", status, SafePreview(bodyText));
                        throw new HttpRequestException($"Failed to parse Sage response (Status {status}).", jsonEx, (HttpStatusCode)status);
                    }

                    if (!sageId.HasValue)
                    {
                        _logger.LogWarning("Sage customer created (Status {Status}) but response did not contain an 'id'. Body: {BodyPreview}", status, SafePreview(bodyText));
                        throw new HttpRequestException($"Sage customer created but ID was missing in the response (Status {status}).", null, (HttpStatusCode)status);
                    }

                    customerDataModel.SageId = sageId.Value;
                    customerDataModel.IsSynced = true;
                    customerDataModel.LastSyncedAt = DateTime.UtcNow;

                    apiCallActivity?.SetTag("sage.response.success", true);
                    apiCallActivity?.SetTag("sage.customer_id", sageId);
                }
                catch (Exception ex) // Catch Sage call exceptions
                {
                    _logger.LogError(ex, "Error calling Sage API to create customer {CustomerCode}. Will attempt local save only.", customerDataModel.CustomerCode);
                    customerDataModel.IsSynced = false;

                    var savedCustomer = await _customerRepository.AddAsync(customerDataModel);
                    finalLocalId = savedCustomer?.Id ?? 0; // Handle potential null from repo AddAsync
                    string msg = $"Customer saved locally (ID: {finalLocalId}) but failed to sync with Sage: {ex.Message}";
                    return (false, msg, finalLocalId, customerDataModel.CustomerCode);
                }

                // Persist Locally (after successful Sage sync)
                var finalSavedCustomer = await _customerRepository.AddAsync(customerDataModel);
                finalLocalId = finalSavedCustomer?.Id ?? 0; // Handle potential null
                _logger.LogInformation("Successfully saved local customer record. Local ID: {LocalId}, Sage ID: {SageId}", finalLocalId, customerDataModel.SageId);

                // Kafka Publish (if sync succeeded)
                if (_events != null && customerDataModel.IsSynced && customerDataModel.SageId.HasValue)
                {
                    try
                    {
                        await _events.PublishAsync("customer.created", new
                        {
                            id = customerDataModel.SageId.Value,
                            reference = customerDataModel.CustomerCode,
                            name = customerDataModel.CustomerName,
                            telephone = customerDataModel.Telephone,
                            email = customerDataModel.Email // Publish the original email
                        }, cancellationToken);
                    }
                    catch (Exception kex)
                    {
                        _logger.LogWarning(kex, "Kafka publish failed for created customer code {Code}", customerDataModel.CustomerCode);
                    }
                }

                return (true, "Customer created successfully", finalLocalId, customerDataModel.CustomerCode);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("CreateCustomerAsync was cancelled for code {Code}", customerDataModel.CustomerCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CreateCustomerAsync for {CustomerCode}", customerDataModel.CustomerCode);
                // Ensure local ID is 0 if persistence failed or wasn't reached
                return (false, $"Error creating customer: {ex.Message}", finalLocalId, customerDataModel.CustomerCode);
            }
        }

        /// <summary>
        ///Resolve a Sage customer by code/reference. Returns the OpenAPI-aligned SageCustomer DTO.
        /// Matches ICustomerService interface. Uses correct OData helper.
        /// </summary>
        public async Task<SageCustomer> GetCustomerByCodeAsync(string customerCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(customerCode))
                throw new ArgumentException("Customer code cannot be empty.", nameof(customerCode));

            string eq = OData.S(customerCode); // Use OData.S from Helpers.cs
            string filter = $"reference eq {eq} or code eq {eq} or customer_reference eq {eq}";
            // Expand contacts and emails for the Email mapping
            string url = $"customers?$filter={filter}&$top=1&$expand=contacts($expand=emails)";
            _logger.LogDebug("Fetching customer from Sage using URL: {Url}", url);

            try
            {
                // Use the correct OData helper from Helpers.cs
                var (customers, _) = await GetODataPageAndCountAsync<SageCustomer>(url, cancellationToken);
                var sageCustomer = customers.FirstOrDefault();

                if (sageCustomer == null)
                {
                    _logger.LogWarning("Customer with code '{CustomerCode}' not found on Sage API via filters.", customerCode);
                    throw new KeyNotFoundException($"Customer with code '{customerCode}' not found on Sage API.");
                }

                // Populate the non-serialized Email property
                MapSageCustomerContactEmail(sageCustomer);

                return sageCustomer;
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                _logger.LogError(ex, "Error fetching customer {CustomerCode} from Sage API. Attempting local fallback.", customerCode);

                var localCustomer = await _customerRepository.GetByCodeAsync(customerCode);
                if (localCustomer != null)
                {
                    _logger.LogWarning("Sage API failed, returning local fallback for {CustomerCode}", customerCode);
                    return MapLocalCustomerToSageDto(localCustomer);
                }
                throw new Exception($"Customer {customerCode} not found in Sage or local DB.", ex);
            }
        }

        /// <summary>
        ///Rich composite details by customer code. Uses SageCustomer DTO internally.
        /// Fixes PagedResult initialization. Maps Country.
        /// </summary>
        public async Task<CustomerDetails> GetCustomerDetailsAsync(
            string customerCode,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerCode))
                throw new ArgumentException("Customer code is required.", nameof(customerCode));

            SageCustomer? cust;
            try
            {
                cust = await GetCustomerByCodeAsync(customerCode, ct);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning("Customer {CustomerCode} not found in GetCustomerDetailsAsync.", customerCode);
                throw; // Rethrow KNF to be handled by controller
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed Sage customer lookup for {CustomerCode} in GetCustomerDetailsAsync. Returning empty.", customerCode);
                return CreateEmptyCustomerDetails(customerCode, page, pageSize);
            }

            // --- Map from SageCustomer DTO ---
            var details = new CustomerDetails
            {
                CustomerReference = cust.Reference ?? customerCode,
                Name = cust.Name ?? "",
                Email = cust.Email, // Use the Email property populated by MapSageCustomerContactEmail
                Telephone = cust.TelephoneSubscriberNumber,
                Addresses = new List<CustomerAddress>(),
                // Use property initializers for PagedResult (from CustomerDetails.cs)
                RecentInvoices = new Models.Customers.PagedResult<InvoiceSummary>
                {
                    Items = new List<InvoiceSummary>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize
                },
                RecentAllocations = new List<AllocationSummary>(),
                OutstandingBalance = cust.Balance ?? 0m
            };

            if (cust.MainAddress != null)
            {
                details.Addresses.Add(new CustomerAddress
                {
                    Type = "Primary",
                    Line1 = cust.MainAddress.Address1 ?? "",
                    Line2 = cust.MainAddress.Address2,
                    City = cust.MainAddress.City,
                    Postcode = cust.MainAddress.Postcode,
                    Country = cust.MainAddress.Country // Map the Country property
                });
            }

            // --- Enrichment logic (Placeholder) ---
            long? accountId = cust.Id;
            if (accountId.HasValue)
            {
                try
                {
                    // TODO: Implement logic to fetch transactions and populate RecentInvoices
                    _logger.LogInformation("Transaction fetch placeholder skipped for {CustomerCode} in Stage 10.", customerCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch transaction history for customer {CustomerCode}", customerCode);
                }
            }

            return details;
        }

        // --- Helper Methods ---

        /// <summary>
        ///Maps the local Data.Models.Customer (used by interface)
        /// to the Sage DTO (SageCustomer) for serialization. Includes Contact mapping for Email.
        /// </summary>
        private SageCustomer MapToSageCustomerCreate(Customer dto)
        {
            var sageCustomer = new SageCustomer
            {
                Reference = dto.CustomerCode,
                Name = dto.CustomerName,
                TelephoneSubscriberNumber = dto.Telephone,
                Website = dto.Email, // Retain original mapping for Website field
                MainAddress = (string.IsNullOrWhiteSpace(dto.AddressLine1) && string.IsNullOrWhiteSpace(dto.City) && string.IsNullOrWhiteSpace(dto.Postcode)) ? null : new SageAddress
                {
                    Address1 = dto.AddressLine1,
                    Address2 = dto.AddressLine2,
                    City = dto.City,
                    Postcode = dto.Postcode
                    // Country and AddressCountryCodeId would need to be sourced if required for create
                }
            };

            // Create Contact structure for Email
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                sageCustomer.Contacts = new List<SageCustomerContact>
                {
                    new SageCustomerContact
                    {
                        // Provide minimal required/useful info if creating contact just for email
                        FirstName = "Primary", // Placeholder
                        LastName = dto.CustomerName, // Placeholder
                        IsDefault = true,
                        Emails = new List<SageCustomerEmail>
                        {
                            new SageCustomerEmail { Email = dto.Email, IsDefault = true }
                        }
                    }
                };
            }
            return sageCustomer;
        }

        /// <summary>
        ///Maps a local DB Customer model to a SageCustomer DTO for fallback scenarios.
        /// Populates non-serialized Email property.
        /// </summary>
        private SageCustomer MapLocalCustomerToSageDto(Customer local)
        {
            var sageDto = new SageCustomer
            {
                Id = local.SageId,
                Reference = local.CustomerCode,
                Name = local.CustomerName,
                TelephoneSubscriberNumber = local.Telephone,
                MainAddress = new SageAddress
                {
                    Address1 = local.AddressLine1,
                    Address2 = local.AddressLine2,
                    City = local.City,
                    Postcode = local.Postcode
                },
                // Set the non-serialized Email property directly for fallback consistency
                Email = local.Email
            };
            // No contacts to map in fallback from local DB model
            return sageDto;
        }

        /// <summary>
        ///Helper to fetch and parse an OData page using OData.MaterializePagedFlexible.
        /// </summary>
        private async Task<(List<T> Items, int Total)> GetODataPageAndCountAsync<T>(string url, CancellationToken ct) where T : class
        {
            try
            {
                using var doc = await _sageApiClient.GetAsync<JsonDocument>(url, ct);
                // Use the helper from Helpers.cs
                var (items, total) = OData.MaterializePagedFlexible<T>(doc);
                return (items.ToList(), total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or parse OData page from URL {Url}", url);
                throw; // Rethrow standard exceptions
            }
        }

        /// <summary>
        /// [NEW] Helper to populate the non-serialized 'Email' property on SageCustomer from its Contacts list.
        /// </summary>
        private void MapSageCustomerContactEmail(SageCustomer customer)
        {
            if (customer == null) return;

            // Try to find the default contact's default email
            var defaultEmail = customer.Contacts?
                .FirstOrDefault(c => c.IsDefault == true)?
                .Emails?
                .FirstOrDefault(e => e.IsDefault == true)?
                .Email;

            // Fallback: first email on first contact
            if (string.IsNullOrWhiteSpace(defaultEmail))
            {
                defaultEmail = customer.Contacts?
                    .FirstOrDefault()?
                    .Emails?
                    .FirstOrDefault()?
                    .Email;
            }

            customer.Email = defaultEmail;
        }


        /// <summary>
        ///Helper to create an empty CustomerDetails for graceful degradation.
        /// Fixes PagedResult initialization.
        /// </summary>
        private CustomerDetails CreateEmptyCustomerDetails(string customerCode, int page, int pageSize)
        {
            // Use property initializers for PagedResult (from CustomerDetails.cs)
            return new CustomerDetails
            {
                CustomerReference = customerCode,
                Name = "Not Found / Error",
                Addresses = new List<CustomerAddress>(),
                RecentInvoices = new Models.Customers.PagedResult<InvoiceSummary>
                {
                    Items = new List<InvoiceSummary>(),
                    TotalCount = 0, // Explicitly set TotalCount
                    Page = page,
                    PageSize = pageSize
                },
                RecentAllocations = new List<AllocationSummary>()
            };
        }

        public async Task<(bool Success, string Message, long? SageCustomerId, string CustomerCode)>
    UpsertCustomerAsync(Customer customer, HttpContext http, CancellationToken ct = default)
        {
            if (customer is null) throw new ArgumentNullException(nameof(customer));
            if (http is null) throw new ArgumentNullException(nameof(http));

            // Try find in Sage by code/reference
            SageCustomer? existing = null;
            try
            {
                existing = await GetCustomerByCodeAsync(customer.CustomerCode, ct);
            }
            catch (KeyNotFoundException)
            {
                existing = null;
            }

            if (existing is null)
            {
                // Not found → create
                var (ok, msg, localId, code) = await CreateCustomerAsync(customer, http, ct);
                return (ok, ok ? "Created" : msg, ok ? customer.SageId : null, code);
            }

            // Found → (minimal) update if desired; for now we no-op and return found id/code
            return (true, "Exists", existing.Id, existing.Reference ?? customer.CustomerCode);
        }

        public async Task<(bool Success, string Message, long? SageCustomerId, string CustomerCode)>
    UpsertCustomerAsync(CustomerPayload customer, RequestContext context, CancellationToken ct = default)
        {
            if (customer is null)
                throw new ArgumentNullException(nameof(customer));

            var model = new Customer
            {
                //CustomerCode = customer.CustomerCode,
                //CustomerName = customer.CustomerName,
                //Email = customer.Email,
                //Telephone = customer.Telephone,
                //AddressLine1 = customer.AddressLine1,
                //AddressLine2 = customer.AddressLine2,
                //City = customer.City,
                //Postcode = customer.Postcode
            };

            // Use existing logic (reuse the HttpContext version internally)
            var fakeHttp = new DefaultHttpContext();
            return await UpsertCustomerAsync(model, fakeHttp, ct);
        }

        public async Task<(bool Success, string Message, long? SopOrderId, string? SopOrderRef, string? SalesInvoiceUrn)>
            CreateInvoiceFromSopAsync(SopOrderCreate sopOrder, HttpContext http, CancellationToken ct = default)
        {
            if (sopOrder is null) throw new ArgumentNullException(nameof(sopOrder));
            if (http is null) throw new ArgumentNullException(nameof(http));

            // 1) Ensure customer is present (if header includes code/id you already validated externally)
            if (!string.IsNullOrWhiteSpace(sopOrder.Header?.CustomerReference))
            {
                var customerModel = new Customer
                {
                    CustomerCode = sopOrder.Header.CustomerReference,
                    CustomerName = sopOrder.Header.CustomerName ?? sopOrder.Header.CustomerReference,
                    Email = sopOrder.Header.CustomerEmail,
                    Telephone = sopOrder.Header.CustomerTelephone,
                    AddressLine1 = sopOrder.Header.AddressLine1,
                    AddressLine2 = sopOrder.Header.AddressLine2,
                    City = sopOrder.Header.City,
                    Postcode = sopOrder.Header.Postcode
                };

                await UpsertCustomerAsync(customerModel, http, ct);
            }

            // 2) Create SOP order
            var sopResult = await _sopOrders.CreateSopOrderAsync(sopOrder, http, ct);
            if (!sopResult.Success || sopResult.OrderId is null)
            {
                return (false, $"SOP create failed: {sopResult.Message}", null, null, null);
            }

            // 3) (Optional) Generate invoice:
            // If your flow needs invoices generated immediately from SOP, either:
            //   a) call a Sage endpoint for “generate invoice from order”, or
            //   b) construct a SalesInvoiceCreate from the SOP header/lines.
            // For now, we’ll **defer** invoice generation (per your pipeline note) and return SOP data only.
            // If you want immediate creation, uncomment and implement the mapping below.

            string? invoiceUrn = null;

            // Example (disabled by default):
            // var invCreate = new SalesInvoiceCreate { /* TODO: map from sopOrder */ };
            // var context = new RequestContext {
            //   SiteId = http.Request.Headers["X-Site"],
            //   CompanyId = http.Request.Headers["X-Company"],
            //   IdempotencyKey = http.Request.Headers["Idempotency-Key"],
            //   CorrelationId = http.TraceIdentifier
            // };
            // var invResult = await _salesInvoices.CreateAsync(invCreate, context, ct);
            // if (invResult.Success) invoiceUrn = invResult.Urn;

            return (true, "SOP created", sopResult.OrderId, sopResult.OrderReference, invoiceUrn);
        }

        /// <summary>
        ///Hashing helper.
        /// </summary>
        private static string? HashBase64Url(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        ///Safe log preview helper.
        /// </summary>
        private static string SafePreview(string? s, int max = 512)
        {
            return Helpers.Truncate(s, max); // Use helper from Helpers.cs
        }

    }
}