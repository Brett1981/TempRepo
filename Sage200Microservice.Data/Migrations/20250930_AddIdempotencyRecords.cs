using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Adds IdempotencyRecords table with unique index on KeyHash.
    /// </summary>
    public partial class AddIdempotencyRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                             .Annotation("SqlServer:Identity", "1, 1"),
                    KeyHash = table.Column<string>(type: "nvarchar(88)", maxLength: 88, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResourceId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_IdempotencyRecords", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_KeyHash",
                table: "IdempotencyRecords",
                column: "KeyHash",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IdempotencyRecords");
        }
    }
}
