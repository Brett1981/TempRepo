using Grpc.Core;
using Sage200Microservice.API.Protos.Invoice;
using Sage200Microservice.Data.Models;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;

namespace Sage200Microservice.API.Services
{
    public class InvoiceGrpcService : InvoiceService.InvoiceServiceBase
    {
        private readonly ILogger<InvoiceGrpcService> _logger;
        private readonly ICustomerService _customerService;
        private readonly IInvoiceService _invoiceService;
        private readonly IApiLogRepository _apiLogRepository;

        public InvoiceGrpcService(
            ILogger<InvoiceGrpcService> logger,
            ICustomerService customerService,
            IInvoiceService invoiceService,
            IApiLogRepository apiLogRepository)
        {
            _logger = logger;
            _customerService = customerService;
            _invoiceService = invoiceService;
            _apiLogRepository = apiLogRepository;
        }

        public override async Task<CreateCustomerResponse> CreateCustomer(CreateCustomerRequest request, ServerCallContext context)
        {
            _logger.LogInformation("gRPC CreateCustomer called with {CustomerName}", request.CustomerName);

            // Log the API call
            var apiLog = new ApiLog
            {
                Endpoint = "/CreateCustomer",
                RequestMethod = "gRPC",
                RequestPayload = $"CustomerName: {request.CustomerName}, CustomerCode: {request.CustomerCode}",
                Timestamp = DateTime.UtcNow,
                CallerId = context.RequestHeaders?.GetValue("caller-id") ?? "Unknown",
                ApiType = "gRPC"
            };

            try
            {
                // Convert gRPC request to our internal model
                var customer = new Customer
                {
                    CustomerName = request.CustomerName,
                    CustomerCode = request.CustomerCode,
                    AddressLine1 = request.AddressLine1,
                    AddressLine2 = request.AddressLine2,
                    City = request.City,
                    Postcode = request.Postcode,
                    Telephone = request.Telephone,
                    Email = request.Email,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = context.RequestHeaders?.GetValue("caller-id") ?? "Unknown"
                };

                // Call our service to create the customer
                var result = await _customerService.CreateCustomerAsync(customer);

                // Update API log with response
                apiLog.ResponsePayload = $"Success: {result.Success}, Message: {result.Message}, CustomerId: {result.CustomerId}";
                apiLog.HttpStatusCode = result.Success ? 200 : 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Return the gRPC response
                return new CreateCustomerResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    CustomerId = (int)result.CustomerId,
                    CustomerCode = result.CustomerCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in gRPC CreateCustomer");

                // Update API log with error response
                apiLog.ResponsePayload = ex.Message;
                apiLog.HttpStatusCode = 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Return error response
                return new CreateCustomerResponse
                {
                    Success = false,
                    Message = $"Error creating customer: {ex.Message}"
                };
            }
        }

        public override async Task<InvoiceStatusResponse> CheckInvoiceStatus(InvoiceStatusRequest request, ServerCallContext context)
        {
            _logger.LogInformation("gRPC CheckInvoiceStatus called for invoice {InvoiceReference}", request.InvoiceReference);

            // Log the API call
            var apiLog = new ApiLog
            {
                Endpoint = "/CheckInvoiceStatus",
                RequestMethod = "gRPC",
                RequestPayload = $"InvoiceReference: {request.InvoiceReference}",
                Timestamp = DateTime.UtcNow,
                CallerId = context.RequestHeaders?.GetValue("caller-id") ?? "Unknown",
                ApiType = "gRPC"
            };

            try
            {
                // Call our service to check the invoice status
                var result = await _invoiceService.CheckInvoiceStatusAsync(request.InvoiceReference);

                // Update API log with response
                apiLog.ResponsePayload = $"Success: {result.Success}, Message: {result.Message}, IsPaid: {result.IsPaid}";
                apiLog.HttpStatusCode = result.Success ? 200 : 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Convert to gRPC response
                var response = new InvoiceStatusResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    InvoiceReference = request.InvoiceReference,
                    IsPaid = result.IsPaid,
                    IsCredited = result.IsCredited,
                    OutstandingValue = (double)result.OutstandingValue,
                    AllocatedValue = (double)result.AllocatedValue,
                    GrossValue = (double)result.OutstandingValue + (double)result.AllocatedValue // Use the sum of outstanding and allocated values
                };

                // Add allocation history to response
                if (result.AllocationHistory != null)
                {
                    foreach (var item in result.AllocationHistory)
                    {
                        response.AllocationHistory.Add(new TransactionHistory
                        {
                            TransactionType = item.trader_transaction_type,
                            TransactionReference = item.allocation_reference,
                            AllocatedValue = (double)item.allocated_value,
                            Date = item.allocation_date.HasValue ? item.allocation_date.Value.ToString("dd-MM-yyyy") : string.Empty
                        });
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in gRPC CheckInvoiceStatus for invoice {InvoiceReference}", request.InvoiceReference);

                // Update API log with error response
                apiLog.ResponsePayload = ex.Message;
                apiLog.HttpStatusCode = 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Return error response
                return new InvoiceStatusResponse
                {
                    Success = false,
                    Message = $"Error checking invoice status: {ex.Message}",
                    InvoiceReference = request.InvoiceReference
                };
            }
        }

        public override async Task<CreateSalesOrderInvoiceResponse> CreateSalesOrderInvoice(CreateSalesOrderInvoiceRequest request, ServerCallContext context)
        {
            _logger.LogInformation("gRPC CreateSalesOrderInvoice called for customer {CustomerId}", request.CustomerId);

            // Log the API call
            var apiLog = new ApiLog
            {
                Endpoint = "/CreateSalesOrderInvoice",
                RequestMethod = "gRPC",
                RequestPayload = $"CustomerId: {request.CustomerId}, OrderDate: {request.OrderDate}",
                Timestamp = DateTime.UtcNow,
                CallerId = context.RequestHeaders?.GetValue("caller-id") ?? "Unknown",
                ApiType = "gRPC"
            };

            try
            {
                // Convert gRPC request to our internal models
                var invoice = new Invoice
                {
                    CustomerId = (int)request.CustomerId,
                    InvoiceReference = $"SO-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    GrossValue = request.Lines.Sum(l => (decimal)l.Quantity * (decimal)l.UnitPrice),
                    OutstandingValue = request.Lines.Sum(l => (decimal)l.Quantity * (decimal)l.UnitPrice),
                    Status = "Unpaid",
                    CreatedAt = DateTime.UtcNow,
                    LastCheckedAt = DateTime.UtcNow,
                    CreatedBy = context.RequestHeaders?.GetValue("caller-id") ?? "Unknown"
                };

                var lines = request.Lines.Select(l => new Sage200Microservice.Services.Models.OrderLine
                {
                    ProductCode = l.ProductCode,
                    Quantity = (decimal)l.Quantity,
                    UnitPrice = (decimal)l.UnitPrice
                }).ToList();

                // Call our service to create the sales order invoice
                var result = await _invoiceService.CreateSalesOrderInvoiceAsync(invoice, lines);

                // Update API log with response
                apiLog.ResponsePayload = $"Success: {result.Success}, Message: {result.Message}, OrderId: {result.OrderId}";
                apiLog.HttpStatusCode = result.Success ? 200 : 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Return the gRPC response
                return new CreateSalesOrderInvoiceResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    OrderId = (int)result.OrderId,
                    OrderReference = result.OrderReference
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in gRPC CreateSalesOrderInvoice");

                // Update API log with error response
                apiLog.ResponsePayload = ex.Message;
                apiLog.HttpStatusCode = 500;
                await _apiLogRepository.AddAsync(apiLog);

                // Return error response
                return new CreateSalesOrderInvoiceResponse
                {
                    Success = false,
                    Message = $"Error creating sales order invoice: {ex.Message}"
                };
            }
        }
    }
}