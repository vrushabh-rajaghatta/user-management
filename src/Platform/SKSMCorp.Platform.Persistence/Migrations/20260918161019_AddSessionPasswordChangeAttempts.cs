using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SKSMCorp.Platform.Persistence.Migrations
{
    /// <summary>
    /// CRD-C4's attempt limit, L6 (docs/requirements.md, "CRD-C4 — limiting
    /// current-password attempts per session"): consecutive failed
    /// current-password attempts within ONE session, never negative.
    ///
    /// Existing sessions start at 0 through the default, so none is ended or
    /// invalidated by this migration. The count belongs to the session and is
    /// purged with it; it is not part of the credential's lockout state.
    /// </summary>
    public partial class AddSessionPasswordChangeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_password_change_attempts",
                table: "user_session",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_session_failed_password_change_attempts",
                table: "user_session",
                sql: "\"failed_password_change_attempts\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_session_failed_password_change_attempts",
                table: "user_session");

            migrationBuilder.DropColumn(
                name: "failed_password_change_attempts",
                table: "user_session");
        }
    }
}
