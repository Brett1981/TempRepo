using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Adds ExternalIdLinks table for cross-application → Sage ID/URN mapping.
    /// </summary>
    public partial class AddExternalIdLinking : Migration
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

                    // FK: AppId → ApiKeys.Id (Restrict: NO CASCADE)
                    table.ForeignKey(
                        name: "FK_ExternalIdLinks_ApiKeys_AppId",
                        column: x => x.AppId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    // CHECK: must have at least one Sage identifier
                    table.CheckConstraint(
                        name: "CK_ExternalIdLink_SageIdOrUrn",
                        sql: "[SageId] IS NOT NULL OR [SageUrn] IS NOT NULL");
                });

            // Unique (AppId, EntityType, ExternalRef)
            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdLinks_App_Entity_ExternalRef",
                table: "ExternalIdLinks",
                columns: new[] { "AppId", "EntityType", "ExternalRef" },
                unique: true);

            // Reverse lookup indexes (explicitly non-clustered by default)
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
            // Drop indexes and table cleanly.
            migrationBuilder.DropTable(
                name: "ExternalIdLinks");
        }
    }
}