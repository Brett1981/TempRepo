using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHttpReplayToIdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Resource",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseContentType",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseHeaders",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseStatusCode",
                table: "IdempotencyRecords",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Resource",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ResponseContentType",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ResponseHeaders",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ResponseStatusCode",
                table: "IdempotencyRecords");
        }
    }
}
