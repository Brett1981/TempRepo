using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging; // Keep this 'using'
using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data
{
    public class ApplicationContext : DbContext
    {
        // REMOVED: private readonly IConfiguration? _configuration;
        // REMOVED: private readonly ILogger<ApplicationContext>? _logger;

        /// <summary>
        /// This is the primary constructor used by ASP.NET Core DI via AddDbContext.
        /// It receives options pre-configured in Program.cs.
        /// </summary>
        public ApplicationContext(DbContextOptions<ApplicationContext> options) : base(options)
        {
        }

        // REMOVED: The (options, configuration, logger) constructor.

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceStatusHistory> InvoiceStatusHistories { get; set; }
        public DbSet<ApiLog> ApiLogs { get; set; }
        public DbSet<ApiKey> ApiKeys { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
        public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();
        public DbSet<TransactionAttempt> TransactionAttempts => Set<TransactionAttempt>();


        /// <summary>
        /// Cross-application → Sage identifier links (source of truth).
        /// </summary>
        public DbSet<ExternalIdLink> ExternalIdLinks { get; set; } = null!;


        //
        // REMOVED: The entire 'OnConfiguring' method.
        // The DbContext is now configured *only* in Program.cs.
        //

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Customers
            modelBuilder.Entity<Customer>(b =>
            {
                b.ToTable("Customers");
                b.HasKey(x => x.Id);
                b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
                b.Property(x => x.CustomerCode).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.CustomerCode).IsUnique();
                b.Property(x => x.AddressLine1).HasMaxLength(200);
                b.Property(x => x.AddressLine2).HasMaxLength(200);
                b.Property(x => x.City).HasMaxLength(100);
                b.Property(x => x.Postcode).HasMaxLength(20);
                b.Property(x => x.Telephone).HasMaxLength(50);
                b.Property(x => x.Email).HasMaxLength(200);
                b.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.CreatedBy).HasMaxLength(100).HasDefaultValue("System");
                b.Property(x => x.SageId).HasMaxLength(100);
                b.Property(x => x.LastSyncedAt).HasColumnType("datetime2");
            });

            // Invoices
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("Invoices");
                b.HasKey(x => x.Id);

                b.Property(x => x.InvoiceReference).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.InvoiceReference).IsUnique();

                b.Property(x => x.GrossValue).HasColumnType("decimal(18,2)");
                b.Property(x => x.OutstandingValue).HasColumnType("decimal(18,2)");

                b.Property(x => x.Status).HasMaxLength(30).IsRequired();

                b.Property(x => x.CreatedAt).HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.LastCheckedAt).HasColumnType("datetime2");

                b.Property(x => x.CreatedBy).HasMaxLength(100).HasDefaultValue("System");
                b.Property(x => x.SageId).HasMaxLength(100);
                b.Property(x => x.LastSyncedAt).HasColumnType("datetime2");

                // Make FK required and bind it to the navigation
                b.Property(i => i.CustomerId).IsRequired();

                b.HasOne(i => i.Customer)           // <-- use the navigation here
                 .WithMany()                        // (no back-collection on Customer)
                 .HasForeignKey(i => i.CustomerId)  // single FK column
                 .OnDelete(DeleteBehavior.Restrict);// Restrict because FK is NOT NULL
            });

            // Invoice Status History
            modelBuilder.Entity<InvoiceStatusHistory>(b =>
            {
                b.ToTable("InvoiceStatusHistories");
                b.HasKey(x => x.Id);
                b.Property(x => x.InvoiceReference).HasMaxLength(50).IsRequired();
                b.Property(x => x.OutstandingValue).HasColumnType("decimal(18,2)");
                b.Property(x => x.AllocatedValue).HasColumnType("decimal(18,2)").HasDefaultValue(0);
                b.Property(x => x.GrossValue).HasColumnType("decimal(18,2)");
                b.Property(x => x.Status).HasMaxLength(30).IsRequired();
                b.Property(x => x.CheckTimestamp).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.Source).HasMaxLength(30).IsRequired();
                b.Property(x => x.CheckedBy).HasMaxLength(100).HasDefaultValue("System");
                b.Property(x => x.CorrelationId).HasMaxLength(64);
                b.HasIndex(x => new { x.InvoiceReference, x.CheckTimestamp });
            });

            // ApiLogs
            modelBuilder.Entity<ApiLog>(b =>
            {
                b.ToTable("ApiLogs");
                b.HasKey(x => x.Id);
                b.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
                b.Property(x => x.RequestMethod).HasMaxLength(10).IsRequired();
                b.Property(x => x.HttpStatusCode).IsRequired();
                b.Property(x => x.Timestamp).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.CallerId).HasMaxLength(100);
                b.Property(x => x.ApiType).HasMaxLength(30);
                b.HasIndex(x => x.Timestamp);
            });

            // ApiKeys
            modelBuilder.Entity<ApiKey>(b =>
            {
                b.ToTable("ApiKeys");
                b.HasKey(x => x.Id);
                b.Property(x => x.Key).HasMaxLength(200).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
                b.Property(x => x.ClientName).HasMaxLength(100).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.ExpiresAt).HasColumnType("datetime2");
                b.Property(x => x.LastUsedAt).HasColumnType("datetime2");
                b.Property(x => x.PreviousKey).HasMaxLength(200);
                b.Property(x => x.PreviousKeyExpiresAt).HasColumnType("datetime2");
                b.Property(x => x.GracePeriodEnd).HasColumnType("datetime2");
                b.Property(x => x.Version).HasDefaultValue(1);
                // AllowedIpAddresses: NVARCHAR(MAX), JSON or CSV – keep as string
            });

            // AuditLogs
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.ToTable("AuditLogs");
                b.HasKey(x => x.Id); // bigint identity
                b.Property(x => x.Timestamp).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");

                // Store enums as strings for readability
                b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(50).IsRequired();
                b.Property(x => x.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
                b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

                b.Property(x => x.UserId).HasMaxLength(100);
                b.Property(x => x.ClientId).HasMaxLength(100);
                b.Property(x => x.IpAddress).HasMaxLength(45);
                b.Property(x => x.Resource).HasMaxLength(100);
                b.Property(x => x.Action).HasMaxLength(100);
                b.Property(x => x.CorrelationId).HasMaxLength(64);
                b.Property(x => x.HttpMethod).HasMaxLength(10);
                b.Property(x => x.UrlPath).HasMaxLength(2048);
                b.Property(x => x.UserAgent).HasMaxLength(512);
                b.Property(x => x.ExpiresAt).HasColumnType("datetime2");
                b.HasIndex(x => x.Timestamp);
                b.HasIndex(x => x.CorrelationId);
            });

            // IdempotencyRecord mapping
            modelBuilder.Entity<IdempotencyRecord>(b =>
            {
                b.ToTable("IdempotencyRecords");
                b.HasKey(x => x.Id);
                b.Property(x => x.KeyHash).IsRequired().HasMaxLength(88); // SHA512-Base64 hash
                b.HasIndex(x => x.KeyHash).IsUnique();
                b.Property(x => x.ResultSageUrn).HasMaxLength(128); // Match ExternalIdLink
            });

            // ------------------------------ ExternalIdLink configuration ------------------------------
            modelBuilder.Entity<ExternalIdLink>(entity =>
            {
                entity.ToTable("ExternalIdLinks");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                // Persist enum as NVARCHAR(40)
                entity.Property(e => e.EntityType)
                      .HasConversion<string>()
                      .HasMaxLength(40)
                      .IsRequired();

                entity.Property(e => e.ExternalRef)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(e => e.SageUrn)
                      .HasMaxLength(128); // Confirmed 128

                entity.Property(e => e.CreatedUtc)
                      .HasPrecision(7)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
                // --- New allocation tracking columns ---
                entity.Property(e => e.IsFullyAllocated)
                    .HasDefaultValue(false);

                entity.Property(e => e.AllocatedValue)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(e => e.OutstandingValue)
                    .HasPrecision(18, 2)
                    .IsRequired(false);

                entity.Property(e => e.LastAllocationCheckUtc)
                    .IsRequired(false);

                entity.Property(e => e.LastAllocationChangeUtc)
                    .IsRequired(false);

                // FK: AppId → ApiKeys.Id (Restrict: NO CASCADE)
                entity.HasOne<ApiKey>()
                      .WithMany()
                      .HasForeignKey(e => e.AppId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Uniqueness by (AppId, EntityType, ExternalRef)
                entity.HasIndex(e => new { e.AppId, e.EntityType, e.ExternalRef })
                      .IsUnique();

                // Reverse lookup indexes (non-clustered)
                entity.HasIndex(e => new { e.EntityType, e.SageId });
                entity.HasIndex(e => new { e.EntityType, e.SageUrn });

                // CHECK: at least one Sage identifier present
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_ExternalIdLink_SageIdOrUrn",
                    "[SageId] IS NOT NULL OR [SageUrn] IS NOT NULL"));
            });

            // Add TransactionAttempts configuration (from your migration)
            modelBuilder.Entity<TransactionAttempt>(entity =>
            {
                entity.ToTable("TransactionAttempts");

                entity.HasKey(e => e.Id);
                entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
                entity.Property(e => e.ReceivedTimestamp).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(e => e.SourceSystem).HasMaxLength(50).IsRequired();
                entity.Property(e => e.TriggeringEventId).HasMaxLength(200).IsRequired();
                entity.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Payload).HasColumnType("varbinary(max)");
                entity.Property(e => e.ProcessingStatus).HasMaxLength(50).IsRequired();
                entity.Property(e => e.KafkaTopic).HasMaxLength(100);
                entity.Property(e => e.KafkaMessageKey).HasMaxLength(200);
                entity.Property(e => e.SiteId).HasMaxLength(64);
                entity.Property(e => e.CompanyId).HasMaxLength(32);
                entity.Property(e => e.IdempotencyKeyHash).HasMaxLength(88);
                entity.Property(e => e.ExternalRef).HasMaxLength(200);
                entity.Property(e => e.ProcessingStartedUtc).HasColumnType("datetime2");
                entity.Property(e => e.ProcessingCompletedUtc).HasColumnType("datetime2");
                entity.Property(e => e.AttemptNumber).HasDefaultValue(1);
                entity.Property(e => e.RetryCount).HasDefaultValue(0);
                entity.Property(e => e.SageUrn).HasMaxLength(128);
                entity.Property(e => e.ResultCode).HasMaxLength(50);
                entity.Property(e => e.ResultMessage).HasMaxLength(1024);
                entity.Property(e => e.OriginalHeadersJson).HasMaxLength(4000);

                // Foreign Key to ApiKeys
                entity.HasOne<ApiKey>()
                      .WithMany()
                      .HasForeignKey(e => e.ApiKeyId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Unique Kafka constraint
                entity.HasIndex(e => new { e.KafkaTopic, e.KafkaPartition, e.KafkaOffset })
                      .IsUnique()
                      .HasFilter("[KafkaTopic] IS NOT NULL AND [KafkaPartition] IS NOT NULL AND [KafkaOffset] IS NOT NULL")
                      .HasDatabaseName("UX_TransactionAttempts_KafkaUnique");

                // Indexes
                entity.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_TransactionAttempts_CorrelationId");

                entity.HasIndex(e => e.IdempotencyKeyHash)
                      .HasFilter("[IdempotencyKeyHash] IS NOT NULL")
                      .HasDatabaseName("IX_TransactionAttempts_IdempotencyKeyHash");

                entity.HasIndex(e => new { e.EntityType, e.SageUrn })
                      .HasFilter("[SageUrn] IS NOT NULL")
                      .HasDatabaseName("IX_TransactionAttempts_EntityType_SageUrn");

                entity.HasIndex(e => new { e.EntityType, e.SageId })
                      .HasFilter("[SageId] IS NOT NULL")
                      .HasDatabaseName("IX_TransactionAttempts_EntityType_SageId");

                entity.HasIndex(e => new { e.SourceSystem, e.TriggeringEventId }) // Corrected property name
                      .HasDatabaseName("IX_TransactionAttempts_SourceSystem_TriggeringEventId");

                entity.HasIndex(e => e.ProcessingStatus)
                      .HasFilter("[ProcessingStatus] IN (N'Received', N'Validated', N'SageCallAttempted')")
                      .HasDatabaseName("IX_TransactionAttempts_ProcessingStatus_InProgress");
            });

            // You need to ensure the TransactionAttempt model class exists
            // in Sage200Microservice.Data.Models
            modelBuilder.Entity<TransactionAttempt>();
        }
    }
}