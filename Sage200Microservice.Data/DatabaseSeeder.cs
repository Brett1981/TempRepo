using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sage200Microservice.Data.Models;
using System.Security.Cryptography;

namespace Sage200Microservice.Data
{
    /// <summary>
    /// Seeds the database with initial data
    /// </summary>
    public static class DatabaseSeeder
    {
        /// <summary>
        /// Seeds the database with initial data
        /// </summary>
        public static async Task SeedDatabaseAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var context = services.GetRequiredService<ApplicationContext>();
                var logger = services.GetRequiredService<ILogger<ApplicationContext>>();

                // Apply migrations
                logger.LogInformation("Applying database migrations");
                await context.Database.MigrateAsync();

                // Seed data
                await SeedApiKeysAsync(context, logger);
                await SeedCustomersAsync(context, logger);
                await SeedInvoicesAsync(context, logger);
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<ApplicationContext>>();
                logger.LogError(ex, "An error occurred while seeding the database");
                throw;
            }
        }

        /// <summary>
        /// Seeds API keys
        /// </summary>
        private static async Task SeedApiKeysAsync(ApplicationContext context, ILogger logger)
        {
            if (!context.ApiKeys.Any())
            {
                logger.LogInformation("Seeding API keys");

                var apiKey = new ApiKey
                {
                    // If you prefer a fixed dev key, replace GenerateApiKey() with "admin-api-key"
                    Key = GenerateApiKey(),
                    ClientName = "Default Client",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    AllowedIpAddresses = "[]" // <<< important: column is NOT NULL
                };

                context.ApiKeys.Add(apiKey);
                await context.SaveChangesAsync();

                logger.LogInformation("Created default API key: {Key}", apiKey.Key);
            }
        }

        /// <summary>
        /// Seeds customers
        /// </summary>
        private static async Task SeedCustomersAsync(ApplicationContext context, ILogger logger)
        {
            if (!context.Customers.Any())
            {
                logger.LogInformation("Seeding customers");

                var customers = new[]
                {
                    new Customer
                    {
                        CustomerName = "Acme Corporation",
                        CustomerCode = "ACME001",
                        AddressLine1 = "123 Main Street",
                        City = "London",
                        Postcode = "EC1A 1BB",
                        Telephone = "+44 20 1234 5678",
                        Email = "info@acme.com",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System",
                        IsSynced = true
                    },
                    new Customer
                    {
                        CustomerName = "Globex Corporation",
                        CustomerCode = "GLOBEX001",
                        AddressLine1 = "456 High Street",
                        City = "Manchester",
                        Postcode = "M1 1AA",
                        Telephone = "+44 161 1234 5678",
                        Email = "info@globex.com",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System",
                        IsSynced = true
                    },
                    new Customer
                    {
                        CustomerName = "Initech",
                        CustomerCode = "INIT001",
                        AddressLine1 = "789 Park Avenue",
                        City = "Birmingham",
                        Postcode = "B1 1AA",
                        Telephone = "+44 121 1234 5678",
                        Email = "info@initech.com",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System",
                        IsSynced = true
                    }
                };

                context.Customers.AddRange(customers);
                await context.SaveChangesAsync();

                logger.LogInformation("Created {Count} sample customers", customers.Length);
            }
        }

        /// <summary>
        /// Seeds invoices
        /// </summary>
        private static async Task SeedInvoicesAsync(ApplicationContext context, ILogger logger)
        {
            if (!context.Invoices.Any())
            {
                logger.LogInformation("Seeding invoices");

                var customers = await context.Customers.ToListAsync();

                if (customers.Any())
                {
                    var invoices = new[]
                    {
                        new Invoice
                        {
                            InvoiceReference = "INV-2025-001",
                            CustomerId = customers[0].Id,
                            GrossValue = 1000.00m,
                            OutstandingValue = 1000.00m,
                            Status = "Unpaid",
                            CreatedAt = DateTime.UtcNow.AddDays(-10),
                            LastCheckedAt = DateTime.UtcNow.AddDays(-10),
                            CreatedBy = "System",
                            IsSynced = true
                        },
                        new Invoice
                        {
                            InvoiceReference = "INV-2025-002",
                            CustomerId = customers[0].Id,
                            GrossValue = 2000.00m,
                            OutstandingValue = 0.00m,
                            Status = "Paid",
                            CreatedAt = DateTime.UtcNow.AddDays(-20),
                            LastCheckedAt = DateTime.UtcNow.AddDays(-1),
                            CreatedBy = "System",
                            IsSynced = true
                        },
                        new Invoice
                        {
                            InvoiceReference = "INV-2025-003",
                            CustomerId = customers[1].Id,
                            GrossValue = 3000.00m,
                            OutstandingValue = 1500.00m,
                            Status = "PartiallyPaid",
                            CreatedAt = DateTime.UtcNow.AddDays(-15),
                            LastCheckedAt = DateTime.UtcNow.AddDays(-1),
                            CreatedBy = "System",
                            IsSynced = true
                        }
                    };

                    context.Invoices.AddRange(invoices);
                    await context.SaveChangesAsync();

                    var statusHistories = new[]
                    {
                        new InvoiceStatusHistory
                        {
                            InvoiceReference = "INV-2025-001",
                            GrossValue = 1000.00m,
                            OutstandingValue = 1000.00m,
                            AllocatedValue = 0.00m,
                            Status = "Unpaid",
                            CheckTimestamp = DateTime.UtcNow.AddDays(-10),
                            Source = "Manual",
                            CheckedBy = "System"
                        },
                        new InvoiceStatusHistory
                        {
                            InvoiceReference = "INV-2025-002",
                            GrossValue = 2000.00m,
                            OutstandingValue = 0.00m,
                            AllocatedValue = 2000.00m,
                            Status = "Paid",
                            CheckTimestamp = DateTime.UtcNow.AddDays(-1),
                            Source = "Manual",
                            CheckedBy = "System"
                        },
                        new InvoiceStatusHistory
                        {
                            InvoiceReference = "INV-2025-003",
                            GrossValue = 3000.00m,
                            OutstandingValue = 1500.00m,
                            AllocatedValue = 1500.00m,
                            Status = "PartiallyPaid",
                            CheckTimestamp = DateTime.UtcNow.AddDays(-1),
                            Source = "Manual",
                            CheckedBy = "System"
                        }
                    };

                    context.InvoiceStatusHistories.AddRange(statusHistories);
                    await context.SaveChangesAsync();

                    logger.LogInformation("Created {Count} sample invoices with status histories", invoices.Length);
                }
            }
        }

        /// <summary>
        /// Generates a random API key
        /// </summary>
        private static string GenerateApiKey()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);

            return Convert.ToBase64String(bytes)
                .Replace("/", "_")
                .Replace("+", "-")
                .Replace("=", "");
        }
    }
}