using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// USR-C4/C5 D12 (docs/requirements.md, "USR-C4 / USR-C5"): status and
    /// deactivation stamp agree on app_user and user_identity.
    ///
    /// Deliberately NO data repair. A database holding a row that violates the
    /// check refuses this migration, and that is the correct outcome: the row
    /// was written wrongly by something, and that something is what gets fixed.
    /// The one known source was a test fixture, corrected where it is written.
    /// </summary>
    public partial class AddStatusDeactivationChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_user_identity_status_deactivation",
                table: "user_identity",
                sql: "(\"status\" = 'Inactive') = (\"deactivated_at\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_status_deactivation",
                table: "app_user",
                sql: "(\"status\" = 'Inactive') = (\"deactivated_at\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_identity_status_deactivation",
                table: "user_identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_status_deactivation",
                table: "app_user");
        }
    }
}
