using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sage200Microservice.Data.Repositories;
using Sage200Microservice.Services.Interfaces;
using Sage200Microservice.Services.Models;
using System.Collections.Concurrent;

namespace Sage200Microservice.Services.Implementations
{
    /// <summary>
    /// Background service for checking invoice status
    /// </summary>
    public class InvoiceStatusBackgroundService : BackgroundService
    {
        private readonly ILogger<InvoiceStatusBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly InvoiceStatusServiceSettings _settings;
        private readonly ConcurrentDictionary<string, DateTime> _processedInvoices = new();

        public InvoiceStatusBackgroundService(
            ILogger<InvoiceStatusBackgroundService> logger,
            IServiceProvider serviceProvider,
            IOptions<InvoiceStatusServiceSettings> settings)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settings = settings.Value ?? new InvoiceStatusServiceSettings();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("Invoice Status Background Service is disabled by configuration.");
                return;
            }

            _logger.LogInformation(
                "Invoice Status Background Service started: interval={Interval}m, batch={Batch}, parallel={Parallel}, maxDegree={Max}",
                _settings.IntervalMinutes, _settings.BatchSize, _settings.EnableParallelProcessing, _settings.MaxDegreeOfParallelism);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutstandingInvoicesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing outstanding invoices.");
                }

                CleanupProcessedInvoices();
                await Task.Delay(TimeSpan.FromMinutes(_settings.IntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Invoice Status Background Service is stopping.");
        }

        private async Task ProcessOutstandingInvoicesAsync(CancellationToken token)
        {
            using var scope = _serviceProvider.CreateScope();
            var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

            var outstanding = await invoiceRepository.GetOutstandingInvoicesAsync();
            var minRecheckMinutes = Math.Max(0, _settings.RetryDelayMinutes); // reuse as throttle

            var toProcess = outstanding
                .Where(i => !_processedInvoices.TryGetValue(i.InvoiceReference, out var last) ||
                            (DateTime.UtcNow - last).TotalMinutes > minRecheckMinutes)
                .Take(_settings.BatchSize)
                .ToList();

            _logger.LogInformation("Processing {Count} of {Total} outstanding invoices.", toProcess.Count.ToString(), outstanding.Count().ToString());

            if (_settings.EnableParallelProcessing)
            {
                var po = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _settings.MaxDegreeOfParallelism),
                    CancellationToken = token
                };

                await Parallel.ForEachAsync(toProcess, po, async (inv, ct) =>
                {
                    try
                    {
                        _logger.LogDebug("Checking status for invoice {Ref}", inv.InvoiceReference);
                        var result = await invoiceService.CheckInvoiceStatusAsync(inv.InvoiceReference);
                        _processedInvoices[inv.InvoiceReference] = DateTime.UtcNow;

                        if (!result.Success)
                            _logger.LogWarning("Invoice {Ref} check failed: {Msg}", inv.InvoiceReference, result.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing invoice {Ref}", inv.InvoiceReference);
                    }
                });
            }
            else
            {
                foreach (var inv in toProcess)
                {
                    try
                    {
                        _logger.LogDebug("Checking status for invoice {Ref}", inv.InvoiceReference);
                        var result = await invoiceService.CheckInvoiceStatusAsync(inv.InvoiceReference);
                        _processedInvoices[inv.InvoiceReference] = DateTime.UtcNow;

                        if (!result.Success)
                            _logger.LogWarning("Invoice {Ref} check failed: {Msg}", inv.InvoiceReference, result.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing invoice {Ref}", inv.InvoiceReference);
                    }
                }
            }
        }

        private void CleanupProcessedInvoices()
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var keys = _processedInvoices.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
            foreach (var k in keys) _processedInvoices.TryRemove(k, out _);

            if (keys.Count > 0)
                _logger.LogDebug("Cleaned {Count} cache entries.", keys.Count);
        }
    }
}