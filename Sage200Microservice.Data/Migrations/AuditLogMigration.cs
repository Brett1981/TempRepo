using Microsoft.EntityFrameworkCore.Migrations;

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Migration to add the AuditLog table
    /// </summary>
    public partial class AuditLogMigration : Migration
    {
        /// <summary>
        /// Upgrades the database
        /// </summary>
        /// <param name="migrationBuilder"> The migration builder </param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the AuditLogs table
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Resource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    UrlPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReferenceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReferenceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PreviousState = table.Column<string>(type: "TEXT", nullable: true),
                    NewState = table.Column<string>(type: "TEXT", nullable: true),
                    RetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            // Add indexes for frequently queried columns
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EventType",
                table: "AuditLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Category",
                table: "AuditLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Severity",
                table: "AuditLogs",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Status",
                table: "AuditLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ClientId",
                table: "AuditLogs",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Resource",
                table: "AuditLogs",
                column: "Resource");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CorrelationId",
                table: "AuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ReferenceId",
                table: "AuditLogs",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ExpiresAt",
                table: "AuditLogs",
                column: "ExpiresAt");

            // Add index for PreviousKey in ApiKeys table
            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_PreviousKey",
                table: "ApiKeys",
                column: "PreviousKey");
        }

        /// <summary>
        /// Downgrades the database
        /// </summary>
        /// <param name="migrationBuilder"> The migration builder </param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the AuditLogs table
            migrationBuilder.DropTable(
                name: "AuditLogs");

            // Drop the index for PreviousKey in ApiKeys table
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_PreviousKey",
                table: "ApiKeys");
        }
    }
}