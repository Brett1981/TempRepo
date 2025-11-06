using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Sage200Microservice.API.Monitoring
{
    /// <summary>
    /// Configuration options for error monitoring and alerting
    /// </summary>
    public class ErrorMonitoringOptions
    {
        /// <summary>
        /// Gets or sets whether error monitoring is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the error severity levels to monitor
        /// </summary>
        public List<string> MonitoredSeverityLevels { get; set; } = new List<string> { "Error", "Critical", "Fatal" };

        /// <summary>
        /// Gets or sets the notification channels
        /// </summary>
        public NotificationChannels NotificationChannels { get; set; } = new NotificationChannels();

        /// <summary>
        /// Gets or sets the alert thresholds
        /// </summary>
        public AlertThresholds AlertThresholds { get; set; } = new AlertThresholds();

        /// <summary>
        /// Gets or sets the alert rules
        /// </summary>
        public List<AlertRule> AlertRules { get; set; } = new List<AlertRule>();

        /// <summary>
        /// Gets or sets the error aggregation window in minutes
        /// </summary>
        public int AggregationWindowMinutes { get; set; } = 5;

        /// <summary>
        /// Gets or sets the minimum time between alerts in minutes
        /// </summary>
        public int MinimumAlertIntervalMinutes { get; set; } = 15;

        /// <summary>
        /// Gets or sets whether to include stack traces in alerts
        /// </summary>
        public bool IncludeStackTraces { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of errors to include in a single alert
        /// </summary>
        public int MaxErrorsPerAlert { get; set; } = 10;
    }

    /// <summary>
    /// Configuration for notification channels
    /// </summary>
    public class NotificationChannels
    {
        /// <summary>
        /// Gets or sets the email notification settings
        /// </summary>
        public EmailNotificationSettings Email { get; set; } = new EmailNotificationSettings();

        /// <summary>
        /// Gets or sets the Slack notification settings
        /// </summary>
        public SlackNotificationSettings Slack { get; set; } = new SlackNotificationSettings();

        /// <summary>
        /// Gets or sets the webhook notification settings
        /// </summary>
        public WebhookNotificationSettings Webhook { get; set; } = new WebhookNotificationSettings();
    }

    /// <summary>
    /// Email notification settings
    /// </summary>
    public class EmailNotificationSettings
    {
        /// <summary>
        /// Gets or sets whether email notifications are enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the SMTP server
        /// </summary>
        public string SmtpServer { get; set; } = "smtp.example.com";

        /// <summary>
        /// Gets or sets the SMTP port
        /// </summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// Gets or sets the SMTP username
        /// </summary>
        public string SmtpUsername { get; set; } = "alerts@example.com";

        /// <summary>
        /// Gets or sets the SMTP password
        /// </summary>
        public string SmtpPassword { get; set; } = "password";

        /// <summary>
        /// Gets or sets whether to use SSL
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// Gets or sets the sender email address
        /// </summary>
        public string FromAddress { get; set; } = "alerts@example.com";

        /// <summary>
        /// Gets or sets the recipient email addresses
        /// </summary>
        public List<string> ToAddresses { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the email subject template
        /// </summary>
        public string SubjectTemplate { get; set; } = "[{Severity}] {ApplicationName} Alert: {ErrorCount} errors detected";
    }

    /// <summary>
    /// Slack notification settings
    /// </summary>
    public class SlackNotificationSettings
    {
        /// <summary>
        /// Gets or sets whether Slack notifications are enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the webhook URL
        /// </summary>
        public string WebhookUrl { get; set; } = "https://hooks.slack.com/services/your/webhook/url";

        /// <summary>
        /// Gets or sets the channel
        /// </summary>
        public string Channel { get; set; } = "#alerts";

        /// <summary>
        /// Gets or sets the username
        /// </summary>
        public string Username { get; set; } = "Error Monitor";

        /// <summary>
        /// Gets or sets the icon emoji
        /// </summary>
        public string IconEmoji { get; set; } = ":warning:";
    }

    /// <summary>
    /// Webhook notification settings
    /// </summary>
    public class WebhookNotificationSettings
    {
        /// <summary>
        /// Gets or sets whether webhook notifications are enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the webhook URL
        /// </summary>
        public string Url { get; set; } = "https://example.com/webhook";

        /// <summary>
        /// Gets or sets the authentication header name
        /// </summary>
        public string AuthHeaderName { get; set; } = "Authorization";

        /// <summary>
        /// Gets or sets the authentication header value
        /// </summary>
        public string AuthHeaderValue { get; set; } = "Bearer your-token";
    }

    /// <summary>
    /// Alert thresholds configuration
    /// </summary>
    public class AlertThresholds
    {
        /// <summary>
        /// Gets or sets the error count threshold
        /// </summary>
        public int ErrorCountThreshold { get; set; } = 5;

        /// <summary>
        /// Gets or sets the error rate threshold (errors per minute)
        /// </summary>
        public double ErrorRateThreshold { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the error percentage threshold (percentage of total requests)
        /// </summary>
        public double ErrorPercentageThreshold { get; set; } = 5.0;
    }

    /// <summary>
    /// Alert rule configuration
    /// </summary>
    public class AlertRule
    {
        /// <summary>
        /// Gets or sets the rule name
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the rule description
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets or sets the error message pattern to match
        /// </summary>
        public string ErrorMessagePattern { get; set; } = "";

        /// <summary>
        /// Gets or sets the severity levels to match
        /// </summary>
        public List<string> SeverityLevels { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the error count threshold for this rule
        /// </summary>
        public int? ErrorCountThreshold { get; set; }

        /// <summary>
        /// Gets or sets the priority level (1-5, where 1 is highest)
        /// </summary>
        public int PriorityLevel { get; set; } = 3;

        /// <summary>
        /// Gets or sets the notification channels to use for this rule
        /// </summary>
        public List<string> NotificationChannels { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service for monitoring errors and sending alerts
    /// </summary>
    public class ErrorMonitoringService : BackgroundService
    {
        private readonly ILogger<ErrorMonitoringService> _logger;
        private readonly ErrorMonitoringOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, DateTime> _lastAlertTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, List<ErrorEvent>> _errorBuffer = new Dictionary<string, List<ErrorEvent>>();
        private readonly string _applicationName;

        public ErrorMonitoringService(
            ILogger<ErrorMonitoringService> logger,
            IOptions<ErrorMonitoringOptions> options,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _applicationName = configuration["Serilog:Properties:Application"] ?? "Sage200Microservice";
        }

        /// <summary>
        /// Executes the error monitoring service
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Error monitoring service is disabled");
                return;
            }

            _logger.LogInformation("Error monitoring service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessErrorBuffers();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing error buffers");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("Error monitoring service stopped");
        }

        /// <summary>
        /// Processes error buffers and sends alerts if thresholds are exceeded
        /// </summary>
        private async Task ProcessErrorBuffers()
        {
            var now = DateTime.UtcNow;
            var cutoffTime = now.AddMinutes(-_options.AggregationWindowMinutes);

            foreach (var key in _errorBuffer.Keys.ToList())
            {
                // Remove old errors
                _errorBuffer[key] = _errorBuffer[key].Where(e => e.Timestamp >= cutoffTime).ToList();

                // Check if we need to send an alert
                if (_errorBuffer[key].Count >= _options.AlertThresholds.ErrorCountThreshold)
                {
                    // Check if we've sent an alert recently
                    if (!_lastAlertTimes.TryGetValue(key, out var lastAlertTime) ||
                        (now - lastAlertTime).TotalMinutes >= _options.MinimumAlertIntervalMinutes)
                    {
                        await SendAlert(key, _errorBuffer[key]);
                        _lastAlertTimes[key] = now;
                    }
                }
            }
        }

        /// <summary>
        /// Sends an alert for the specified errors
        /// </summary>
        private async Task SendAlert(string key, List<ErrorEvent> errors)
        {
            _logger.LogInformation("Sending alert for {ErrorCount} errors with key {Key}", errors.Count, key);

            // Limit the number of errors included in the alert
            var errorsToInclude = errors.OrderByDescending(e => e.Timestamp).Take(_options.MaxErrorsPerAlert).ToList();

            // Find matching alert rules
            var matchingRules = _options.AlertRules
                .Where(r => string.IsNullOrEmpty(r.ErrorMessagePattern) ||
                           errors.Any(e => e.Message.Contains(r.ErrorMessagePattern)))
                .Where(r => !r.SeverityLevels.Any() ||
                           errors.Any(e => r.SeverityLevels.Contains(e.Severity)))
                .Where(r => !r.ErrorCountThreshold.HasValue ||
                           errors.Count >= r.ErrorCountThreshold.Value)
                .OrderBy(r => r.PriorityLevel)
                .ToList();

            // If no specific rules match, use default channels
            var notificationChannels = matchingRules.Any()
                ? matchingRules.SelectMany(r => r.NotificationChannels).Distinct().ToList()
                : new List<string> { "Email", "Slack", "Webhook" };

            // Send alerts to each enabled channel
            var tasks = new List<Task>();

            if (notificationChannels.Contains("Email") && _options.NotificationChannels.Email.Enabled)
            {
                tasks.Add(SendEmailAlert(errorsToInclude));
            }

            if (notificationChannels.Contains("Slack") && _options.NotificationChannels.Slack.Enabled)
            {
                tasks.Add(SendSlackAlert(errorsToInclude));
            }

            if (notificationChannels.Contains("Webhook") && _options.NotificationChannels.Webhook.Enabled)
            {
                tasks.Add(SendWebhookAlert(errorsToInclude));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Sends an email alert
        /// </summary>
        private Task SendEmailAlert(List<ErrorEvent> errors)
        {
            // In a real implementation, this would send an email using the configured SMTP settings
            _logger.LogInformation("Email alert would be sent to {Recipients} for {ErrorCount} errors",
                string.Join(", ", _options.NotificationChannels.Email.ToAddresses),
                errors.Count);

            // This is a placeholder for the actual email sending logic
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends a Slack alert
        /// </summary>
        private async Task SendSlackAlert(List<ErrorEvent> errors)
        {
            try
            {
                var settings = _options.NotificationChannels.Slack;
                if (string.IsNullOrEmpty(settings.WebhookUrl))
                {
                    _logger.LogWarning("Slack webhook URL is not configured");
                    return;
                }

                var client = _httpClientFactory.CreateClient("SlackNotifications");

                var highestSeverity = errors.Select(e => e.Severity).OrderByDescending(s => s).FirstOrDefault() ?? "Error";

                var message = new
                {
                    channel = settings.Channel,
                    username = settings.Username,
                    icon_emoji = settings.IconEmoji,
                    text = $"*{highestSeverity} Alert*: {errors.Count} errors detected in {_applicationName}",
                    attachments = new[]
                    {
                        new
                        {
                            color = highestSeverity == "Fatal" ? "danger" :
                                   highestSeverity == "Critical" ? "danger" :
                                   highestSeverity == "Error" ? "warning" : "good",
                            title = $"Error Details ({errors.Count} errors)",
                            text = string.Join("\n\n", errors.Select(e =>
                                $"*{e.Timestamp:yyyy-MM-dd HH:mm:ss} UTC - {e.Severity}*\n" +
                                $"{e.Message}" +
                                (_options.IncludeStackTraces && !string.IsNullOrEmpty(e.StackTrace)
                                    ? $"\n```{e.StackTrace.Substring(0, Math.Min(e.StackTrace.Length, 500))}```"
                                    : "")
                            )),
                            footer = $"{_applicationName} | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(settings.WebhookUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to send Slack alert: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Slack alert");
            }
        }

        /// <summary>
        /// Sends a webhook alert
        /// </summary>
        private async Task SendWebhookAlert(List<ErrorEvent> errors)
        {
            try
            {
                var settings = _options.NotificationChannels.Webhook;
                if (string.IsNullOrEmpty(settings.Url))
                {
                    _logger.LogWarning("Webhook URL is not configured");
                    return;
                }

                var client = _httpClientFactory.CreateClient("WebhookNotifications");

                if (!string.IsNullOrEmpty(settings.AuthHeaderName) && !string.IsNullOrEmpty(settings.AuthHeaderValue))
                {
                    client.DefaultRequestHeaders.Add(settings.AuthHeaderName, settings.AuthHeaderValue);
                }

                var highestSeverity = errors.Select(e => e.Severity).OrderByDescending(s => s).FirstOrDefault() ?? "Error";

                var payload = new
                {
                    application = _applicationName,
                    timestamp = DateTime.UtcNow,
                    severity = highestSeverity,
                    errorCount = errors.Count,
                    errors = errors.Select(e => new
                    {
                        timestamp = e.Timestamp,
                        severity = e.Severity,
                        message = e.Message,
                        stackTrace = _options.IncludeStackTraces ? e.StackTrace : null
                    }).ToList()
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(settings.Url, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to send webhook alert: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending webhook alert");
            }
        }

        /// <summary>
        /// Records an error event
        /// </summary>
        public void RecordError(string severity, string message, string stackTrace = null)
        {
            if (!_options.Enabled || !_options.MonitoredSeverityLevels.Contains(severity))
            {
                return;
            }

            var errorEvent = new ErrorEvent
            {
                Timestamp = DateTime.UtcNow,
                Severity = severity,
                Message = message,
                StackTrace = stackTrace
            };

            // Use the error type as the key
            var key = GetErrorKey(errorEvent);

            lock (_errorBuffer)
            {
                if (!_errorBuffer.ContainsKey(key))
                {
                    _errorBuffer[key] = new List<ErrorEvent>();
                }

                _errorBuffer[key].Add(errorEvent);
            }
        }

        /// <summary>
        /// Gets a key for grouping similar errors
        /// </summary>
        private string GetErrorKey(ErrorEvent errorEvent)
        {
            // Extract the error type from the message or stack trace This is a simple
            // implementation that could be improved
            var message = errorEvent.Message ?? "";
            var firstLine = message.Split('\n').FirstOrDefault() ?? "";
            var errorType = firstLine.Split(':').FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(errorType) && !string.IsNullOrEmpty(errorEvent.StackTrace))
            {
                var stackTrace = errorEvent.StackTrace;
                var firstStackLine = stackTrace.Split('\n').FirstOrDefault() ?? "";
                errorType = firstStackLine.Split(':').FirstOrDefault() ?? "";
            }

            return string.IsNullOrWhiteSpace(errorType) ? "UnknownError" : errorType.Trim();
        }
    }

    /// <summary>
    /// Represents an error event
    /// </summary>
    public class ErrorEvent
    {
        /// <summary>
        /// Gets or sets the timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the severity
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the stack trace
        /// </summary>
        public string StackTrace { get; set; }
    }

    /// <summary>
    /// Extension methods for configuring error monitoring
    /// </summary>
    public static class ErrorMonitoringExtensions
    {
        /// <summary>
        /// Adds error monitoring services to the service collection
        /// </summary>
        public static IServiceCollection AddErrorMonitoring(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ErrorMonitoringOptions>(configuration.GetSection("ErrorMonitoring"));
            services.AddHttpClient("SlackNotifications");
            services.AddHttpClient("WebhookNotifications");
            services.AddSingleton<ErrorMonitoringService>();
            services.AddHostedService(provider => provider.GetRequiredService<ErrorMonitoringService>());

            return services;
        }
    }
}