using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalLinking_10102025 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalIdLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SageId = table.Column<long>(type: "bigint", nullable: true),
                    SageUrn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdLinks", x => x.Id);
                    table.CheckConstraint("CK_ExternalIdLink_SageIdOrUrn", "[SageId] IS NOT NULL OR [SageUrn] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ExternalIdLinks_ApiKeys_AppId",
                        column: x => x.AppId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdLinks_AppId_EntityType_ExternalRef",
                table: "ExternalIdLinks",
                columns: new[] { "AppId", "EntityType", "ExternalRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdLinks_EntityType_SageId",
                table: "ExternalIdLinks",
                columns: new[] { "EntityType", "SageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdLinks_EntityType_SageUrn",
                table: "ExternalIdLinks",
                columns: new[] { "EntityType", "SageUrn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdLinks");
        }
    }
}
