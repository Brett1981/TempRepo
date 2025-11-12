using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;                // Invoice, OrderLine
using Sage200Microservice.Services.Interfaces;        // IInvoiceService
using Sage200Microservice.Services.Models;            // SageAllocationHistoryItem
using System.ComponentModel.DataAnnotations;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Sales invoices – create, check status, and run outstanding processing.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class InvoicesController : ControllerBase
    {
        private readonly ILogger<InvoicesController> _logger;
        private readonly IInvoiceService _invoiceService;

        public InvoicesController(ILogger<InvoicesController> logger, IInvoiceService invoiceService)
        {
            _logger = logger;
            _invoiceService = invoiceService;
        }

        /// <summary>
        /// Create a sales order invoice in Sage 200.
        /// </summary>
        /// <remarks>
        /// Provide the invoice header and its lines. The service handles the Sage call.
        /// </remarks>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(CreateInvoiceResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<CreateInvoiceResponse>> Create([FromBody] CreateInvoiceRequest request)
        {
            if (request is null || request.Invoice is null || request.Lines is null || request.Lines.Count == 0)
            {
                return BadRequest(new ErrorResponse { Message = "Invoice and at least one line are required." });
            }

            try
            {
                // If you have an identifying field on Invoice (e.g., CustomerId/CustomerRef),
                // you can add it here for context:
                // _logger.LogInformation("Creating invoice for CustomerId={CustomerId}", request.Invoice.CustomerId);

                var (success, message, orderId, orderRef) =
                    await _invoiceService.CreateSalesOrderInvoiceAsync(request.Invoice, request.Lines);

                var resp = new CreateInvoiceResponse
                {
                    Success = success,
                    Message = message,
                    OrderId = orderId,
                    OrderReference = orderRef
                };

                if (!success)
                    return BadRequest(resp);

                // Created at GET …/status (location is a convenience)
                return CreatedAtAction(nameof(GetStatus), new { invoiceReference = orderRef }, resp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sales order invoice.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse { Message = $"Error creating invoice: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get the payment/credit status of an invoice.
        /// </summary>
        [HttpGet("{invoiceReference}/status")]
        [ProducesResponseType(typeof(InvoiceStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<InvoiceStatusResponse>> GetStatus([FromRoute, Required] string invoiceReference)
        {
            if (string.IsNullOrWhiteSpace(invoiceReference))
                return NotFound(new ErrorResponse { Message = "Invoice reference is required." });

            try
            {
                var (success, message, isPaid, isCredited, outstanding, allocated, history) =
                    await _invoiceService.CheckInvoiceStatusAsync(invoiceReference);

                if (!success)
                    return NotFound(new ErrorResponse { Message = message });

                return Ok(new InvoiceStatusResponse
                {
                    Success = true,
                    Message = message,
                    InvoiceReference = invoiceReference,
                    IsPaid = isPaid,
                    IsCredited = isCredited,
                    OutstandingValue = outstanding,
                    AllocatedValue = allocated,
                    AllocationHistory = history ?? new List<SageAllocationHistoryItem>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking status for invoice {Ref}", invoiceReference);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse { Message = $"Error checking invoice status: {ex.Message}" });
            }
        }

        /// <summary>
        /// Processes any outstanding invoices (server-side batch).
        /// </summary>
        [HttpPost("process-outstanding")]
        [ProducesResponseType(typeof(BatchKickResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<BatchKickResponse>> ProcessOutstanding()
        {
            try
            {
                await _invoiceService.ProcessOutstandingInvoicesAsync();

                return Accepted(new BatchKickResponse
                {
                    Accepted = true,
                    Message = "Outstanding invoice processing started."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting outstanding invoice processing.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse { Message = $"Error starting processing: {ex.Message}" });
            }
        }

        // ---------- DTOs ----------

        public sealed class CreateInvoiceRequest
        {
            [Required] public Invoice? Invoice { get; set; }
            [Required] public List<OrderLine>? Lines { get; set; }
        }

        public sealed class CreateInvoiceResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public long OrderId { get; set; }
            public string OrderReference { get; set; } = "";
        }

        public sealed class InvoiceStatusResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string InvoiceReference { get; set; } = "";

            public bool IsPaid { get; set; }
            public bool IsCredited { get; set; }
            public decimal OutstandingValue { get; set; }
            public decimal AllocatedValue { get; set; }
            public List<SageAllocationHistoryItem> AllocationHistory { get; set; } = new();
        }

        public sealed class BatchKickResponse
        {
            public bool Accepted { get; set; }
            public string Message { get; set; } = "";
        }

        public sealed class ErrorResponse
        {
            public string Message { get; set; } = "";
            public string? Details { get; set; }
        }
    }
}
