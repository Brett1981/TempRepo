using Microsoft.Extensions.Options;
using Prometheus;
using Sage200Microservice.Data.Repositories;

namespace Sage200Microservice.API.Metrics
{
    public class BusinessMetricsOptions
    {
        public bool Enabled { get; set; } = true;
        public int CollectionIntervalSeconds { get; set; } = 60;
        public int RetentionDays { get; set; } = 90;

        public List<string> MetricsToCollect { get; set; } = new()
        {
            "TotalCustomers",
            "NewCustomers",
            "TotalInvoices",
            "PendingInvoices",
            "CompletedInvoices",
            "TotalRevenue",
            "AverageInvoiceValue",
            "ApiKeyUsage",
            "EndpointUsage"
        };
    }

    /// <summary>
    /// Collects and exposes business metrics. Uses a new scope per collection cycle and runs EF
    /// operations sequentially to avoid DbContext concurrency issues.
    /// </summary>
    public class BusinessMetricsService : BackgroundService
    {
        private readonly ILogger<BusinessMetricsService> _logger;
        private readonly BusinessMetricsOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly Gauge _totalCustomers = Prometheus.Metrics.CreateGauge(
            "sage200_total_customers", "Total number of customers");

        private readonly Gauge _newCustomers = Prometheus.Metrics.CreateGauge(
            "sage200_new_customers_last_24h", "Number of new customers in the last 24 hours");

        private readonly Gauge _totalInvoices = Prometheus.Metrics.CreateGauge(
            "sage200_total_invoices", "Total number of invoices");

        private readonly Gauge _pendingInvoices = Prometheus.Metrics.CreateGauge(
            "sage200_pending_invoices", "Number of invoices not yet paid");

        private readonly Gauge _completedInvoices = Prometheus.Metrics.CreateGauge(
            "sage200_completed_invoices", "Number of invoices paid");

        private readonly Gauge _totalRevenue = Prometheus.Metrics.CreateGauge(
            "sage200_total_revenue", "Sum of invoice gross values");

        private readonly Gauge _averageInvoiceValue = Prometheus.Metrics.CreateGauge(
            "sage200_average_invoice_value", "Average invoice gross value");

        private readonly Gauge _apiKeyUsage = Prometheus.Metrics.CreateGauge(
            "sage200_api_key_usage", "API key usage count",
            new GaugeConfiguration { LabelNames = new[] { "client_name", "key_id" } });

        private readonly Gauge _endpointUsage = Prometheus.Metrics.CreateGauge(
            "sage200_endpoint_usage", "Endpoint usage count",
            new GaugeConfiguration { LabelNames = new[] { "endpoint", "method" } });

        public BusinessMetricsService(
            ILogger<BusinessMetricsService> logger,
            IOptions<BusinessMetricsOptions> options,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _options = options.Value;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Business metrics service is disabled");
                return;
            }

            _logger.LogInformation("Business metrics service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CollectMetricsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while collecting business metrics");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.CollectionIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("Business metrics service stopped");
        }

        private async Task CollectMetricsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            var apiLogs = scope.ServiceProvider.GetRequiredService<IApiLogRepository>();

            // Run EF calls sequentially to avoid sharing one DbContext concurrently.
            if (_options.MetricsToCollect.Contains("TotalCustomers"))
                await CollectTotalCustomersMetric(customers);

            if (_options.MetricsToCollect.Contains("NewCustomers"))
                await CollectNewCustomersMetric(customers);

            if (_options.MetricsToCollect.Contains("TotalInvoices") ||
                _options.MetricsToCollect.Contains("PendingInvoices") ||
                _options.MetricsToCollect.Contains("CompletedInvoices"))
                await CollectInvoiceMetrics(invoices);

            if (_options.MetricsToCollect.Contains("TotalRevenue") ||
                _options.MetricsToCollect.Contains("AverageInvoiceValue"))
                await CollectRevenueMetrics(invoices);

            if (_options.MetricsToCollect.Contains("ApiKeyUsage"))
                await CollectApiKeyUsageMetrics(apiLogs);

            if (_options.MetricsToCollect.Contains("EndpointUsage"))
                await CollectEndpointUsageMetrics(apiLogs);

            _logger.LogDebug("Business metrics collection completed");
        }

        private async Task CollectTotalCustomersMetric(ICustomerRepository customerRepository)
        {
            try
            {
                var count = await customerRepository.CountAsync();
                _totalCustomers.Set(count);
                _logger.LogDebug("Total customers: {Count}", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting total customers metric");
            }
        }

        private async Task CollectNewCustomersMetric(ICustomerRepository customerRepository)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var count = await customerRepository.CountAsync(c => c.CreatedAt >= since);
                _newCustomers.Set(count);
                _logger.LogDebug("New customers (24h): {Count}", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting new customers metric");
            }
        }

        private async Task CollectInvoiceMetrics(IInvoiceRepository invoiceRepository)
        {
            try
            {
                // Align with your seeded statuses: "Unpaid", "Paid", "PartiallyPaid"
                var total = await invoiceRepository.CountAsync();
                var completed = await invoiceRepository.CountAsync(i => i.Status == "Paid");
                var pending = await invoiceRepository.CountAsync(i => i.Status != "Paid");

                _totalInvoices.Set(total);
                _completedInvoices.Set(completed);
                _pendingInvoices.Set(pending);

                _logger.LogDebug("Invoices => total:{Total} completed(Paid):{Completed} pending(!Paid):{Pending}",
                    total, completed, pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting invoice metrics");
            }
        }

        private async Task CollectRevenueMetrics(IInvoiceRepository invoiceRepository)
        {
            try
            {
                // Use GrossValue for totals/average (matches your seed data)
                var items = (await invoiceRepository.GetAllAsync()).ToList();
                var totalRevenue = items.Sum(i => i.GrossValue);
                var avg = items.Count > 0 ? items.Average(i => i.GrossValue) : 0m;

                _totalRevenue.Set((double)totalRevenue);
                _averageInvoiceValue.Set((double)avg);

                _logger.LogDebug("Revenue => total:{Total} avg:{Avg} (count:{Count})",
                    totalRevenue, avg, items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting revenue metrics");
            }
        }

        private async Task CollectApiKeyUsageMetrics(IApiLogRepository apiLogRepository)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var logs = await apiLogRepository.GetAllAsync(l => l.Timestamp >= since);

                var byCaller = logs
                    .GroupBy(l => string.IsNullOrWhiteSpace(l.CallerId) ? "unknown" : l.CallerId)
                    .Select(g => new { Caller = g.Key, Count = g.Count() });

                foreach (var row in byCaller)
                {
                    _apiKeyUsage.WithLabels(row.Caller, row.Caller).Set(row.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting API key usage metrics");
            }
        }

        private async Task CollectEndpointUsageMetrics(IApiLogRepository apiLogRepository)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var logs = await apiLogRepository.GetAllAsync(l => l.Timestamp >= since);

                var byEndpoint = logs
                    .GroupBy(l => new
                    {
                        Endpoint = string.IsNullOrWhiteSpace(l.Endpoint) ? "unknown" : l.Endpoint,
                        Method = string.IsNullOrWhiteSpace(l.RequestMethod) ? "unknown" : l.RequestMethod
                    })
                    .Select(g => new { g.Key.Endpoint, g.Key.Method, Count = g.Count() });

                foreach (var row in byEndpoint)
                {
                    _endpointUsage.WithLabels(row.Endpoint, row.Method).Set(row.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting endpoint usage metrics");
            }
        }
    }

    public static class BusinessMetricsExtensions
    {
        public static IServiceCollection AddBusinessMetrics(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<BusinessMetricsOptions>(configuration.GetSection("BusinessMetrics"));
            services.AddHostedService<BusinessMetricsService>();
            return services;
        }
    }
}