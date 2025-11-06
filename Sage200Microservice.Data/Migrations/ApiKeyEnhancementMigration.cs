using Microsoft.EntityFrameworkCore.Migrations;

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Migration to enhance the ApiKey table with IP filtering and key rotation support
    /// </summary>
    public partial class ApiKeyEnhancementMigration : Migration
    {
        /// <summary>
        /// Upgrades the database
        /// </summary>
        /// <param name="migrationBuilder"> The migration builder </param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns to the ApiKey table
            migrationBuilder.AddColumn<string>(
                name: "AllowedIpAddresses",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PreviousKey",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousKeyExpiresAt",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);
        }

        /// <summary>
        /// Downgrades the database
        /// </summary>
        /// <param name="migrationBuilder"> The migration builder </param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the new columns from the ApiKey table
            migrationBuilder.DropColumn(
                name: "AllowedIpAddresses",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "PreviousKey",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "PreviousKeyExpiresAt",
                table: "ApiKeys");
        }
    }
}