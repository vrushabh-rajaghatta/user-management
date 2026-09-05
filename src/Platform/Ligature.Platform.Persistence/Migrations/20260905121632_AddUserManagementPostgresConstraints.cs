using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManagementPostgresConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
        CREATE UNIQUE INDEX "ux_app_user_active_human_email"
        ON "app_user" (lower("email"))
        WHERE "actor_type" = 'Human'
          AND "status" <> 'Inactive'
          AND "email" IS NOT NULL;
        """);

            migrationBuilder.Sql(
                """
        CREATE UNIQUE INDEX "ux_app_user_system_actor"
        ON "app_user" ("actor_type")
        WHERE "actor_type" = 'System';
        """);

            migrationBuilder.Sql(
                """
        CREATE UNIQUE INDEX "ux_user_identity_local_username"
        ON "user_identity" (lower("username"))
        WHERE "identity_type" = 'Local'
          AND "username" IS NOT NULL;
        """);

            migrationBuilder.Sql(
                """
        CREATE UNIQUE INDEX "ux_role_permission_active"
        ON "role_permission" ("role_id", "permission_id")
        WHERE "revoked_at" IS NULL;
        """);

            migrationBuilder.Sql(
                """
        CREATE EXTENSION IF NOT EXISTS btree_gist;
        """);

            migrationBuilder.Sql(
                """
        ALTER TABLE "user_role"
        ADD CONSTRAINT "ex_user_role_global_no_overlap"
        EXCLUDE USING gist
        (
            "user_id" WITH =,
            "role_id" WITH =,
            "scope_type" WITH =,
            tstzrange("effective_from", "effective_to", '[)') WITH &&
        )
        WHERE ("scope_id" IS NULL);
        """);

            migrationBuilder.Sql(
                """
        ALTER TABLE "user_role"
        ADD CONSTRAINT "ex_user_role_scoped_no_overlap"
        EXCLUDE USING gist
        (
            "user_id" WITH =,
            "role_id" WITH =,
            "scope_type" WITH =,
            "scope_id" WITH =,
            tstzrange("effective_from", "effective_to", '[)') WITH &&
        )
        WHERE ("scope_id" IS NOT NULL);
        """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
        DROP INDEX IF EXISTS "ux_app_user_active_human_email";
        """);

            migrationBuilder.Sql(
                """
        DROP INDEX IF EXISTS "ux_app_user_system_actor";
        """);

            migrationBuilder.Sql(
                """
        DROP INDEX IF EXISTS "ux_user_identity_local_username";
        """);

            migrationBuilder.Sql(
                """
        DROP INDEX IF EXISTS "ux_role_permission_active";
        """);

            migrationBuilder.Sql(
                """
        ALTER TABLE "user_role"
        DROP CONSTRAINT IF EXISTS "ex_user_role_global_no_overlap";

        ALTER TABLE "user_role"
        DROP CONSTRAINT IF EXISTS "ex_user_role_scoped_no_overlap";
        """);
        }
    }
}
