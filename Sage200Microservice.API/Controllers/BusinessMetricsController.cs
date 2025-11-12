using Microsoft.AspNetCore.Mvc;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Controllers
{
    /// <summary>
    /// Controller for business metrics
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BusinessMetricsController : ControllerBase
    {
        private readonly ILogger<BusinessMetricsController> _logger;
        private readonly ICustomerRepository _customerRepository;
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IApiLogRepository _apiLogRepository;

        public BusinessMetricsController(
            ILogger<BusinessMetricsController> logger,
            ICustomerRepository customerRepository,
            IInvoiceRepository invoiceRepository,
            IApiLogRepository apiLogRepository)
        {
            _logger = logger;
            _customerRepository = customerRepository;
            _invoiceRepository = invoiceRepository;
            _apiLogRepository = apiLogRepository;
        }

        /// <summary>
        /// Gets a summary of business metrics
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<BusinessMetricsSummary>> GetSummary()
        {
            try
            {
                var last24Hours = DateTime.UtcNow.AddHours(-24);
                var last7Days = DateTime.UtcNow.AddDays(-7);
                var last30Days = DateTime.UtcNow.AddDays(-30);

                // Customers
                var totalCustomers = await _customerRepository.CountAsync();
                var newCustomers24h = await _customerRepository.CountAsync(c => c.CreatedAt >= last24Hours);
                var newCustomers7d = await _customerRepository.CountAsync(c => c.CreatedAt >= last7Days);
                var newCustomers30d = await _customerRepository.CountAsync(c => c.CreatedAt >= last30Days);

                // Invoices
                var invoices = (await _invoiceRepository.GetAllAsync()).ToList();
                var totalInvoices = invoices.Count();
                var pendingInvoices = invoices.Count(i => IsPendingLike(i.Status));
                var completedInvoices = invoices.Count(i => IsCompletedLike(i.Status));

                var invoices24h = invoices.Where(i => i.CreatedAt >= last24Hours).ToList();
                var invoices7d = invoices.Where(i => i.CreatedAt >= last7Days).ToList();
                var invoices30d = invoices.Where(i => i.CreatedAt >= last30Days).ToList();

                var totalRevenue = invoices.Sum(i => i.TotalAmount);
                var revenue24h = invoices24h.Sum(i => i.TotalAmount);
                var revenue7d = invoices7d.Sum(i => i.TotalAmount);
                var revenue30d = invoices30d.Sum(i => i.TotalAmount);
                var averageInvoiceValue = totalInvoices > 0 ? totalRevenue / totalInvoices : 0m;

                // API usage (use CallerId and RequestMethod from your ApiLog model)
                var apiLogs = await _apiLogRepository.GetAllAsync(l => l.Timestamp >= last24Hours);

                var apiKeyUsage = apiLogs
                    .GroupBy(l => new { Caller = string.IsNullOrWhiteSpace(l.CallerId) ? "unknown" : l.CallerId })
                    .Select(g => new ApiKeyUsageMetric
                    {
                        // We don’t have ApiKeyId/ClientName on ApiLog; reuse Caller for both labels.
                        KeyId = g.Key.Caller,
                        ClientName = g.Key.Caller,
                        Count = g.Count()
                    })
                    .OrderByDescending(u => u.Count)
                    .Take(10)
                    .ToList();

                var endpointUsage = apiLogs
                    .GroupBy(l => new
                    {
                        Endpoint = string.IsNullOrWhiteSpace(l.Endpoint) ? "unknown" : l.Endpoint,
                        Method = string.IsNullOrWhiteSpace(l.RequestMethod) ? "unknown" : l.RequestMethod
                    })
                    .Select(g => new EndpointUsageMetric
                    {
                        Endpoint = g.Key.Endpoint,
                        Method = g.Key.Method,
                        Count = g.Count()
                    })
                    .OrderByDescending(u => u.Count)
                    .Take(10)
                    .ToList();

                // Summary
                var summary = new BusinessMetricsSummary
                {
                    Timestamp = DateTime.UtcNow,
                    CustomerMetrics = new CustomerMetrics
                    {
                        TotalCustomers = totalCustomers,
                        NewCustomers24h = newCustomers24h,
                        NewCustomers7d = newCustomers7d,
                        NewCustomers30d = newCustomers30d
                    },
                    InvoiceMetrics = new InvoiceMetrics
                    {
                        TotalInvoices = totalInvoices,
                        PendingInvoices = pendingInvoices,
                        CompletedInvoices = completedInvoices,
                        NewInvoices24h = invoices24h.Count(),
                        NewInvoices7d = invoices7d.Count(),
                        NewInvoices30d = invoices30d.Count()
                    },
                    RevenueMetrics = new RevenueMetrics
                    {
                        TotalRevenue = totalRevenue,
                        Revenue24h = revenue24h,
                        Revenue7d = revenue7d,
                        Revenue30d = revenue30d,
                        AverageInvoiceValue = averageInvoiceValue
                    },
                    ApiUsageMetrics = new ApiUsageMetrics
                    {
                        TotalRequests24h = apiLogs.Count(),
                        TopApiKeys = apiKeyUsage,
                        TopEndpoints = endpointUsage
                    }
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting business metrics summary");
                return StatusCode(500, "An error occurred while retrieving business metrics");
            }
        }

        /// <summary>
        /// Gets customer metrics
        /// </summary>
        [HttpGet("customers")]
        public async Task<ActionResult<CustomerMetricsDetail>> GetCustomerMetrics()
        {
            try
            {
                var last30Days = DateTime.UtcNow.AddDays(-30);
                var customers = (await _customerRepository.GetAllAsync()).ToList();

                var totalCustomers = customers.Count();

                var dailyNewCustomers = customers
                    .Where(c => c.CreatedAt >= last30Days)
                    .GroupBy(c => c.CreatedAt.Date)
                    .Select(g => new DailyMetric { Date = g.Key, Value = g.Count() })
                    .OrderBy(m => m.Date)
                    .ToList();

                // Fill missing days
                var result = new List<DailyMetric>();
                for (var date = last30Days.Date; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
                {
                    var metric = dailyNewCustomers.FirstOrDefault(m => m.Date == date);
                    result.Add(new DailyMetric { Date = date, Value = metric?.Value ?? 0 });
                }

                return Ok(new CustomerMetricsDetail
                {
                    TotalCustomers = totalCustomers,
                    DailyNewCustomers = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customer metrics");
                return StatusCode(500, "An error occurred while retrieving customer metrics");
            }
        }

        /// <summary>
        /// Gets invoice metrics
        /// </summary>
        [HttpGet("invoices")]
        public async Task<ActionResult<InvoiceMetricsDetail>> GetInvoiceMetrics()
        {
            try
            {
                var last30Days = DateTime.UtcNow.AddDays(-30);
                var invoices = (await _invoiceRepository.GetAllAsync()).ToList();
                var totalInvoices = invoices.Count;
                var pendingInvoices = invoices.Count(i => IsPendingLike(i.Status));
                var completedInvoices = invoices.Count(i => IsCompletedLike(i.Status));

                var dailyNewInvoices = invoices
                    .Where(i => i.CreatedAt >= last30Days)
                    .GroupBy(i => i.CreatedAt.Date)
                    .Select(g => new DailyMetric { Date = g.Key, Value = g.Count() })
                    .OrderBy(m => m.Date)
                    .ToList();

                var dailyRevenue = invoices
                    .Where(i => i.CreatedAt >= last30Days)
                    .GroupBy(i => i.CreatedAt.Date)
                    .Select(g => new DailyMetric { Date = g.Key, Value = g.Sum(i => i.TotalAmount) })
                    .OrderBy(m => m.Date)
                    .ToList();

                // Fill missing days
                var newInvoicesResult = new List<DailyMetric>();
                var revenueResult = new List<DailyMetric>();

                for (var date = last30Days.Date; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
                {
                    var newInv = dailyNewInvoices.FirstOrDefault(m => m.Date == date);
                    newInvoicesResult.Add(new DailyMetric { Date = date, Value = newInv?.Value ?? 0 });

                    var rev = dailyRevenue.FirstOrDefault(m => m.Date == date);
                    revenueResult.Add(new DailyMetric { Date = date, Value = rev?.Value ?? 0 });
                }

                return Ok(new InvoiceMetricsDetail
                {
                    TotalInvoices = totalInvoices,
                    PendingInvoices = pendingInvoices,
                    CompletedInvoices = completedInvoices,
                    DailyNewInvoices = newInvoicesResult,
                    DailyRevenue = revenueResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting invoice metrics");
                return StatusCode(500, "An error occurred while retrieving invoice metrics");
            }
        }

        /// <summary>
        /// Gets API usage metrics
        /// </summary>
        [HttpGet("api-usage")]
        public async Task<ActionResult<ApiUsageMetricsDetail>> GetApiUsageMetrics()
        {
            try
            {
                var last24Hours = DateTime.UtcNow.AddHours(-24);
                var apiLogs = (await _apiLogRepository.GetAllAsync(l => l.Timestamp >= last24Hours)).ToList();

                var totalRequests = apiLogs.Count();

                var apiKeyUsage = apiLogs
                    .GroupBy(l => new { Caller = string.IsNullOrWhiteSpace(l.CallerId) ? "unknown" : l.CallerId })
                    .Select(g => new ApiKeyUsageMetric
                    {
                        KeyId = g.Key.Caller,
                        ClientName = g.Key.Caller,
                        Count = g.Count()
                    })
                    .OrderByDescending(u => u.Count)
                    .ToList();

                var endpointUsage = apiLogs
                    .GroupBy(l => new
                    {
                        Endpoint = string.IsNullOrWhiteSpace(l.Endpoint) ? "unknown" : l.Endpoint,
                        Method = string.IsNullOrWhiteSpace(l.RequestMethod) ? "unknown" : l.RequestMethod
                    })
                    .Select(g => new EndpointUsageMetric
                    {
                        Endpoint = g.Key.Endpoint,
                        Method = g.Key.Method,
                        Count = g.Count()
                    })
                    .OrderByDescending(u => u.Count)
                    .ToList();

                var hourlyRequests = apiLogs
                    .GroupBy(l => new DateTime(l.Timestamp.Year, l.Timestamp.Month, l.Timestamp.Day, l.Timestamp.Hour, 0, 0))
                    .Select(g => new HourlyMetric { Hour = g.Key, Value = g.Count() })
                    .OrderBy(m => m.Hour)
                    .ToList();

                // Fill missing hours
                var hourlyResult = new List<HourlyMetric>();
                for (var hour = last24Hours; hour <= DateTime.UtcNow; hour = hour.AddHours(1))
                {
                    var roundedHour = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0);
                    var metric = hourlyRequests.FirstOrDefault(m => m.Hour == roundedHour);
                    hourlyResult.Add(new HourlyMetric { Hour = roundedHour, Value = metric?.Value ?? 0 });
                }

                return Ok(new ApiUsageMetricsDetail
                {
                    TotalRequests24h = totalRequests,
                    ApiKeyUsage = apiKeyUsage,
                    EndpointUsage = endpointUsage,
                    HourlyRequests = hourlyResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API usage metrics");
                return StatusCode(500, "An error occurred while retrieving API usage metrics");
            }
        }

        private static bool IsPendingLike(string status)
    => !string.IsNullOrWhiteSpace(status) &&
       (status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Unpaid", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Partial", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("PartiallyPaid", StringComparison.OrdinalIgnoreCase));

        private static bool IsCompletedLike(string status)
            => !string.IsNullOrWhiteSpace(status) &&
               (status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Paid", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Credited", StringComparison.OrdinalIgnoreCase));
    }

    // ======= DTOs used by the controller =======

    public class BusinessMetricsSummary
    {
        public DateTime Timestamp { get; set; }
        public CustomerMetrics CustomerMetrics { get; set; }
        public InvoiceMetrics InvoiceMetrics { get; set; }
        public RevenueMetrics RevenueMetrics { get; set; }
        public ApiUsageMetrics ApiUsageMetrics { get; set; }
    }

    public class CustomerMetrics
    {
        public int TotalCustomers { get; set; }
        public int NewCustomers24h { get; set; }
        public int NewCustomers7d { get; set; }
        public int NewCustomers30d { get; set; }
    }

    public class InvoiceMetrics
    {
        public int TotalInvoices { get; set; }
        public int PendingInvoices { get; set; }
        public int CompletedInvoices { get; set; }
        public int NewInvoices24h { get; set; }
        public int NewInvoices7d { get; set; }
        public int NewInvoices30d { get; set; }
    }

    public class RevenueMetrics
    {
        public decimal TotalRevenue { get; set; }
        public decimal Revenue24h { get; set; }
        public decimal Revenue7d { get; set; }
        public decimal Revenue30d { get; set; }
        public decimal AverageInvoiceValue { get; set; }
    }

    public class ApiUsageMetrics
    {
        public int TotalRequests24h { get; set; }
        public List<ApiKeyUsageMetric> TopApiKeys { get; set; }
        public List<EndpointUsageMetric> TopEndpoints { get; set; }
    }

    public class ApiKeyUsageMetric
    {
        public string KeyId { get; set; }
        public string ClientName { get; set; }
        public int Count { get; set; }
    }

    public class EndpointUsageMetric
    {
        public string Endpoint { get; set; }
        public string Method { get; set; }
        public int Count { get; set; }
    }

    public class DailyMetric
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
    }

    public class HourlyMetric
    {
        public DateTime Hour { get; set; }
        public decimal Value { get; set; }
    }

    public class CustomerMetricsDetail
    {
        public int TotalCustomers { get; set; }
        public List<DailyMetric> DailyNewCustomers { get; set; }
    }

    public class InvoiceMetricsDetail
    {
        public int TotalInvoices { get; set; }
        public int PendingInvoices { get; set; }
        public int CompletedInvoices { get; set; }
        public List<DailyMetric> DailyNewInvoices { get; set; }
        public List<DailyMetric> DailyRevenue { get; set; }
    }

    public class ApiUsageMetricsDetail
    {
        public int TotalRequests24h { get; set; }
        public List<ApiKeyUsageMetric> ApiKeyUsage { get; set; }
        public List<EndpointUsageMetric> EndpointUsage { get; set; }
        public List<HourlyMetric> HourlyRequests { get; set; }
    }
}