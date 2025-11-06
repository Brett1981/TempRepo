using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    public partial class SyncModel_26092025 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0) Drop any default bound to [Invoices].[Id] (defensive for re-runs/partials)
            migrationBuilder.Sql(@"
DECLARE @dc sysname;
SELECT @dc = d.name
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID(N'[Invoices]') AND c.name = N'Id';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [Invoices] DROP CONSTRAINT [' + @dc + ']');
");

            // 1) Drop PK
            migrationBuilder.DropPrimaryKey(
                name: "PK_Invoices",
                table: "Invoices");

            // 2) Add temporary BIGINT column (nullable, NO DEFAULT)
            migrationBuilder.AddColumn<long>(
                name: "Id_tmp",
                table: "Invoices",
                type: "bigint",
                nullable: true); // <— important: nullable and no default

            // 3) Copy values
            migrationBuilder.Sql(@"UPDATE [Invoices] SET [Id_tmp] = CAST([Id] AS bigint);");

            // 4) Make temp NOT NULL
            migrationBuilder.Sql(@"ALTER TABLE [Invoices] ALTER COLUMN [Id_tmp] bigint NOT NULL;");

            // 5) Drop old column
            migrationBuilder.DropColumn(
                name: "Id",
                table: "Invoices");

            // 6) Rename temp -> Id
            migrationBuilder.RenameColumn(
                name: "Id_tmp",
                table: "Invoices",
                newName: "Id");

            // 7) Re-add PK
            migrationBuilder.AddPrimaryKey(
                name: "PK_Invoices",
                table: "Invoices",
                column: "Id");

            // 8) Create/replace the sequence starting at MAX(Id)+1
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Invoices_Id_Seq]', 'SO') IS NOT NULL
    DROP SEQUENCE [dbo].[Invoices_Id_Seq];

DECLARE @startAt bigint = (SELECT ISNULL(MAX([Id]), 0) + 1 FROM [Invoices]);
DECLARE @sql nvarchar(400) = N'CREATE SEQUENCE [dbo].[Invoices_Id_Seq] AS BIGINT START WITH ' + CAST(@startAt AS nvarchar(50)) + N' INCREMENT BY 1';
EXEC(@sql);
");

            // 9) Ensure NO default exists now, then bind default to the sequence
            migrationBuilder.Sql(@"
DECLARE @dc2 sysname;
SELECT @dc2 = d.name
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID(N'[Invoices]') AND c.name = N'Id';
IF @dc2 IS NOT NULL EXEC(N'ALTER TABLE [Invoices] DROP CONSTRAINT [' + @dc2 + ']');

ALTER TABLE [Invoices]
ADD CONSTRAINT [DF_Invoices_Id] DEFAULT (NEXT VALUE FOR [dbo].[Invoices_Id_Seq]) FOR [Id];
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the steps, using the same no-default temp pattern

            // Drop any default bound to Id
            migrationBuilder.Sql(@"
DECLARE @dc sysname;
SELECT @dc = d.name
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID(N'[Invoices]') AND c.name = N'Id';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [Invoices] DROP CONSTRAINT [' + @dc + ']');
");

            // Drop the sequence if present
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Invoices_Id_Seq]', 'SO') IS NOT NULL
    DROP SEQUENCE [dbo].[Invoices_Id_Seq];
");

            // Drop PK
            migrationBuilder.DropPrimaryKey(
                name: "PK_Invoices",
                table: "Invoices");

            // Add temp int column (nullable, NO default)
            migrationBuilder.AddColumn<int>(
                name: "Id_tmp",
                table: "Invoices",
                type: "int",
                nullable: true);

            // Copy & make NOT NULL
            migrationBuilder.Sql(@"UPDATE [Invoices] SET [Id_tmp] = CAST([Id] AS int);");
            migrationBuilder.Sql(@"ALTER TABLE [Invoices] ALTER COLUMN [Id_tmp] int NOT NULL;");

            // Drop old
            migrationBuilder.DropColumn(
                name: "Id",
                table: "Invoices");

            // Rename back
            migrationBuilder.RenameColumn(
                name: "Id_tmp",
                table: "Invoices",
                newName: "Id");

            // Re-add PK
            migrationBuilder.AddPrimaryKey(
                name: "PK_Invoices",
                table: "Invoices",
                column: "Id");

            // Recreate the sequence & default for INT (optional)
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Invoices_Id_Seq]', 'SO') IS NOT NULL
    DROP SEQUENCE [dbo].[Invoices_Id_Seq];

DECLARE @startAt int = (SELECT ISNULL(MAX([Id]), 0) + 1 FROM [Invoices]);
DECLARE @sql nvarchar(400) = N'CREATE SEQUENCE [dbo].[Invoices_Id_Seq] AS INT START WITH ' + CAST(@startAt AS nvarchar(50)) + N' INCREMENT BY 1';
EXEC(@sql);

DECLARE @dc3 sysname;
SELECT @dc3 = d.name
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID(N'[Invoices]') AND c.name = N'Id';
IF @dc3 IS NOT NULL EXEC(N'ALTER TABLE [Invoices] DROP CONSTRAINT [' + @dc3 + ']');

ALTER TABLE [Invoices]
ADD CONSTRAINT [DF_Invoices_Id] DEFAULT (NEXT VALUE FOR [dbo].[Invoices_Id_Seq]) FOR [Id];
");
        }
    }
}
