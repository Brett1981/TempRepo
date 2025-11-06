using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sage200Microservice.Data.Migrations
{
    /// <summary>
    /// Blocks DELETEs on dbo.OAuthTokens via INSTEAD OF DELETE trigger.
    /// Adds a controlled DBA escape hatch proc to allow a single delete in-session.
    /// (TRUNCATE protection should be handled by permissions outside EF.)
    /// </summary>
    public partial class Add_OAuthTokens_DeleteGuard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create or update the INSTEAD OF DELETE trigger (idempotent).
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.trg_OAuthTokens_BlockDelete', N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.trg_OAuthTokens_BlockDelete ON dbo.OAuthTokens INSTEAD OF DELETE AS BEGIN SET NOCOUNT ON; END');
ALTER TRIGGER dbo.trg_OAuthTokens_BlockDelete
ON dbo.OAuthTokens
INSTEAD OF DELETE
AS
BEGIN
SET NOCOUNT ON;

-- Controlled bypass for DBAs: set once per session, then cleared.
IF TRY_CAST(SESSION_CONTEXT(N''AllowOAuthTokensDelete'') AS bit) = 1
BEGIN
    EXEC sys.sp_set_session_context @key = N''AllowOAuthTokensDelete'', @value = NULL;

    DELETE t
    FROM dbo.OAuthTokens AS t
    INNER JOIN deleted AS d ON d.Id = t.Id;

    RETURN;
END;

-- Default: block deletes
THROW 50001, ''Deleting from dbo.OAuthTokens is blocked. Use prc_OAuthTokens_AllowDeleteOnce if intentional.'', 1;


END
");

            // Create/replace the DBA-only one-time escape hatch (idempotent).
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
            // Remove the trigger & proc on downgrade.
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