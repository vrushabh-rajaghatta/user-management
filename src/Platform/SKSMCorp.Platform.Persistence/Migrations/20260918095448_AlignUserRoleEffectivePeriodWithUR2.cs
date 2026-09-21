using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SKSMCorp.Platform.Persistence.Migrations
{
    /// <summary>
    /// Relaxes ck_user_role_effective_period from <c>&gt;</c> to <c>&gt;=</c> to restore
    /// alignment with frozen UR2 (<c>EffectiveTo IS NULL OR EffectiveTo &gt;= EffectiveFrom</c>),
    /// which the initial migration had tightened (docs/requirements.md, "Role
    /// Assignment").
    ///
    /// The stricter constraint made the empty period impossible, and with it
    /// AUT-C2 revoking a future assignment before it takes effect, the
    /// specification's own example. This removes an implementation limit the
    /// contract never had: nothing the old constraint admitted is newly refused.
    /// Equal dates remain invalid for a GRANT; UserRole.Create refuses them.
    ///
    /// Down cannot run once a future assignment has been revoked: that row's
    /// empty period satisfies >= and not >, and PostgreSQL refuses to add a
    /// constraint that an existing row violates. That is correct: reverting
    /// would declare valid history invalid.
    /// </summary>
    public partial class AlignUserRoleEffectivePeriodWithUR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_role_effective_period",
                table: "user_role");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_role_effective_period",
                table: "user_role",
                sql: "\"effective_to\" IS NULL OR \"effective_to\" >= \"effective_from\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_role_effective_period",
                table: "user_role");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_role_effective_period",
                table: "user_role",
                sql: "\"effective_to\" IS NULL OR \"effective_to\" > \"effective_from\"");
        }
    }
}
