using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Ensures the OAuthTokens delete guard (trigger + proc) exist.
    /// Uses separate batches for CREATE/ALTER so SQL Server accepts them.
    /// </summary>
    public partial class Ensure_OAuthTokens_DeleteGuard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Create a no-op trigger stub if it doesn't exist (batch 1)
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.trg_OAuthTokens_BlockDelete', N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.trg_OAuthTokens_BlockDelete ON dbo.OAuthTokens INSTEAD OF DELETE AS BEGIN SET NOCOUNT ON; END');
");

            // 2) Define/replace the trigger logic (must be first in batch) (batch 2)
            migrationBuilder.Sql(@"


ALTER TRIGGER dbo.trg_OAuthTokens_BlockDelete
ON dbo.OAuthTokens
INSTEAD OF DELETE
AS
BEGIN
SET NOCOUNT ON;

-- One-time DBA bypass via proc below; cleared after use.
IF TRY_CAST(SESSION_CONTEXT(N'AllowOAuthTokensDelete') AS bit) = 1
BEGIN
    EXEC sys.sp_set_session_context @key = N'AllowOAuthTokensDelete', @value = NULL;
    DELETE t
    FROM dbo.OAuthTokens AS t
    JOIN deleted AS d ON d.Id = t.Id;
    RETURN;
END;

-- Default: block deletes so refresh tokens are not lost accidentally.
THROW 50001, 'Deleting from dbo.OAuthTokens is blocked. Use prc_OAuthTokens_AllowDeleteOnce if intentional.', 1;


END
");

            // 3) Create or update the bypass proc (first in its own batch) (batch 3)
            migrationBuilder.Sql(@"


CREATE OR ALTER PROCEDURE dbo.prc_OAuthTokens_AllowDeleteOnce
AS
BEGIN
SET NOCOUNT ON;
EXEC sys.sp_set_session_context @key = N'AllowOAuthTokensDelete', @value = 1, @read_only = 0;
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"


IF OBJECT_ID(N'dbo.trg_OAuthTokens_BlockDelete', N'TR') IS NOT NULL
DROP TRIGGER dbo.trg_OAuthTokens_BlockDelete;
");

            migrationBuilder.Sql(@"


IF OBJECT_ID(N'dbo.prc_OAuthTokens_AllowDeleteOnce', N'P') IS NOT NULL
DROP PROCEDURE dbo.prc_OAuthTokens_AllowDeleteOnce;
");
        }
    }
}