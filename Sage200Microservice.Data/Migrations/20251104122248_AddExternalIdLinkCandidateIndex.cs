using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdLinkCandidateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdLinks_AppId_EntityType_IsFullyAllocated_LastAllocationCheckUtc_Id",
                table: "ExternalIdLinks",
                columns: new[]
                {
                    "AppId",
                    "EntityType",
                    "IsFullyAllocated",
                    "LastAllocationCheckUtc",
                    "Id"
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIdLinks_AppId_EntityType_IsFullyAllocated_LastAllocationCheckUtc_Id",
                table: "ExternalIdLinks");
        }
    }
}
