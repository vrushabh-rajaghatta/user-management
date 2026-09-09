using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// Grants the User Management tables to the roles that replaced the
    /// superuser connection.
    ///
    /// Until now every component connected as one superuser, so no grant was
    /// needed and none existed. Splitting the connections is what makes the
    /// audit boundary provable — a superuser walks through every control —
    /// and it means the ordinary tables need explicit privileges too.
    ///
    /// The shape is G1's: SELECT, INSERT and UPDATE, and no DELETE for
    /// anyone. That is safe to assert today because the application performs
    /// no deletes at all: there is no Remove, RemoveRange, ExecuteDelete or
    /// DELETE statement anywhere in src. G1 also names user_session as the
    /// eventual purge exception; no purge exists yet, so no DELETE is granted
    /// for it either, and the entry stays in the Known Gaps list.
    ///
    /// This grants only. G4, PH3 and SP2 — the immutability triggers — remain
    /// deferred and are not implemented here.
    ///
    /// Runs as migration_role, which owns these tables and may therefore
    /// grant on them. It holds nothing on the audit schema, which is owned by
    /// audit_owner and deployed separately (see Audit/AuditSchemaDeployer.cs).
    /// </summary>
    public partial class AddUserManagementPrivilegeModel : Migration
    {
        /// <summary>
        /// Named individually rather than through GRANT ON ALL TABLES, so a
        /// future table gets no privileges by accident: a new table should
        /// have to say who may read it.
        /// </summary>
        private static readonly string[] Tables =
        [
            "app_user",
            "credential",
            "password_history",
            "permission",
            "role",
            "role_permission",
            "security_policy",
            "user_identity",
            "user_role",
            "user_session",
            "user_token",
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql(
                    $"""
                    REVOKE ALL ON "{table}" FROM PUBLIC;
                    GRANT SELECT, INSERT, UPDATE ON "{table}" TO app_role;
                    GRANT SELECT, INSERT, UPDATE ON "{table}" TO provisioning_role;
                    """);
            }

            // Ligature.Provisioning refuses to seed a database with pending
            // migrations, which means reading EF's history table.
            migrationBuilder.Sql(
                """
                GRANT SELECT ON "__EFMigrationsHistory" TO provisioning_role;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql(
                    $"""
                    REVOKE ALL ON "{table}" FROM app_role;
                    REVOKE ALL ON "{table}" FROM provisioning_role;
                    """);
            }

            migrationBuilder.Sql(
                """
                REVOKE ALL ON "__EFMigrationsHistory" FROM provisioning_role;
                """);
        }
    }
}
