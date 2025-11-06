using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionAttempts",
                schema: "dbo", // Assuming dbo schema
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceivedTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    SourceSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TriggeringEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<byte[]>(type: "varbinary(max)", nullable: true), // Encrypted payload
                    ProcessingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    KafkaTopic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    KafkaPartition = table.Column<int>(type: "int", nullable: true),
                    KafkaOffset = table.Column<long>(type: "bigint", nullable: true),
                    KafkaMessageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CompanyId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IdempotencyKeyHash = table.Column<string>(type: "nvarchar(88)", maxLength: 88, nullable: true), // Nullable if not always present
                    ExternalRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApiKeyId = table.Column<int>(type: "int", nullable: true), // FK to ApiKeys
                    ProcessingStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingCompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SageUrn = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SageId = table.Column<long>(type: "bigint", nullable: true),
                    ResultCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OriginalHeadersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionAttempts", x => x.Id);
                    // Unique constraint for Kafka messages where topic, partition, offset are known
                    table.UniqueConstraint("UX_TransactionAttempts_KafkaUnique", x => new { x.KafkaTopic, x.KafkaPartition, x.KafkaOffset })
                         .Annotation("SqlServer:Filter", "[KafkaTopic] IS NOT NULL AND [KafkaPartition] IS NOT NULL AND [KafkaOffset] IS NOT NULL");
                    // Foreign Key to ApiKeys table
                    table.ForeignKey(
                        name: "FK_TransactionAttempts_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys", // Ensure this matches your ApiKeys table name
                        principalColumn: "Id",    // Ensure this matches your ApiKey primary key column name
                        onDelete: ReferentialAction.Restrict); // Or SetNull depending on requirements
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttempts_CorrelationId",
                schema: "dbo",
                table: "TransactionAttempts",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttempts_IdempotencyKeyHash",
                schema: "dbo",
                table: "TransactionAttempts",
                column: "IdempotencyKeyHash")
                .Annotation("SqlServer:Filter", "[IdempotencyKeyHash] IS NOT NULL"); // Index only non-null keys

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttempts_EntityType_SageUrn",
                schema: "dbo",
                table: "TransactionAttempts",
                columns: new[] { "EntityType", "SageUrn" })
                .Annotation("SqlServer:Filter", "[SageUrn] IS NOT NULL"); // Index only rows with URN

            migrationBuilder.CreateIndex(
               name: "IX_TransactionAttempts_EntityType_SageId",
               schema: "dbo",
               table: "TransactionAttempts",
               columns: new[] { "EntityType", "SageId" })
               .Annotation("SqlServer:Filter", "[SageId] IS NOT NULL"); // Index only rows with SageId

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttempts_SourceSystem_TriggeringEventId",
                schema: "dbo",
                table: "TransactionAttempts",
                columns: new[] { "SourceSystem", "TriggeringEventId" });

            // Filtered index for finding in-flight attempts
            migrationBuilder.CreateIndex(
                name: "IX_TransactionAttempts_ProcessingStatus_InProgress",
                schema: "dbo",
                table: "TransactionAttempts",
                column: "ProcessingStatus")
                .Annotation("SqlServer:Filter", "[ProcessingStatus] IN (N'Received', N'Validated', N'SageCallAttempted')"); // Adjust statuses if needed
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionAttempts",
                schema: "dbo");
        }
    }
}