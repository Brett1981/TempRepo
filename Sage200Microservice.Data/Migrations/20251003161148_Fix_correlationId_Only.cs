using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
/// <inheritdoc />
public partial class Fix_correlationId_Only : Migration
{
/// <inheritdoc />
protected override void Up(MigrationBuilder migrationBuilder)
{
// 1) Backfill any NULL CorrelationId values to avoid NOT NULL failures.
migrationBuilder.Sql(@"
UPDATE [dbo].[InvoiceStatusHistories]
SET [CorrelationId] = CONVERT(nvarchar(64), NEWID())
WHERE [CorrelationId] IS NULL;
");

        // 2) Drop existing default constraint on CorrelationId if present (name is unknown).
        migrationBuilder.Sql(@"
DECLARE @ConstraintName sysname;
SELECT @ConstraintName = d.name
FROM sys.default_constraints d
JOIN sys.columns c
ON c.object_id = d.parent_object_id
AND c.column_id = d.parent_column_id
WHERE d.parent_object_id = OBJECT_ID(N'[dbo].[InvoiceStatusHistories]')
AND c.name = N'CorrelationId';
IF @ConstraintName IS NOT NULL
EXEC(N'ALTER TABLE [dbo].[InvoiceStatusHistories] DROP CONSTRAINT [' + @ConstraintName + ']');
");

        // 3) Enforce NOT NULL on CorrelationId (no PK/Id changes).
        migrationBuilder.Sql(@"
ALTER TABLE [dbo].[InvoiceStatusHistories]
ALTER COLUMN [CorrelationId] nvarchar(64) NOT NULL;
");

        // 4) Add a deterministic default for future inserts.
        migrationBuilder.Sql(@"
ALTER TABLE [dbo].[InvoiceStatusHistories]
ADD CONSTRAINT [DF_InvoiceStatusHistories_CorrelationId]
DEFAULT (CONVERT(nvarchar(64), NEWID())) FOR [CorrelationId];
");
}

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverse: drop default and (optionally) allow NULLs again.
        migrationBuilder.Sql(@"
DECLARE @ConstraintName sysname;
SELECT @ConstraintName = d.name
FROM sys.default_constraints d
JOIN sys.columns c
ON c.object_id = d.parent_object_id
AND c.column_id = d.parent_column_id
WHERE d.parent_object_id = OBJECT_ID(N'[dbo].[InvoiceStatusHistories]')
AND c.name = N'CorrelationId';
IF @ConstraintName IS NOT NULL
EXEC(N'ALTER TABLE [dbo].[InvoiceStatusHistories] DROP CONSTRAINT [' + @ConstraintName + ']');
");

        migrationBuilder.Sql(@"
ALTER TABLE [dbo].[InvoiceStatusHistories]
ALTER COLUMN [CorrelationId] nvarchar(64) NULL;
");
}
}
}

