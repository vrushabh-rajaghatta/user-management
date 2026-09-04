using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialUserManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "varchar", nullable: false),
                    first_name = table.Column<string>(type: "varchar", nullable: true),
                    last_name = table.Column<string>(type: "varchar", nullable: true),
                    display_name = table.Column<string>(type: "varchar", nullable: false),
                    email = table.Column<string>(type: "varchar", nullable: true),
                    status = table.Column<string>(type: "varchar", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                    table.UniqueConstraint("AK_app_user_id_actor_type", x => new { x.id, x.actor_type });
                    table.CheckConstraint("ck_app_user_actor_type", "\"actor_type\" IN ('Human', 'Agent', 'System')");
                    table.CheckConstraint("ck_app_user_deactivation_pair", "(\"deactivated_at\" IS NULL) = (\"deactivated_by\" IS NULL)");
                    table.CheckConstraint("ck_app_user_human_names", "\"actor_type\" <> 'Human' OR (\"first_name\" IS NOT NULL AND \"last_name\" IS NOT NULL)");
                    table.CheckConstraint("ck_app_user_status", "\"status\" IN ('Active', 'Inactive')");
                    table.CheckConstraint("ck_app_user_system_not_deactivated", "\"actor_type\" <> 'System' OR \"deactivated_at\" IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "permission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar", nullable: false),
                    name = table.Column<string>(type: "varchar", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    resource = table.Column<string>(type: "varchar", nullable: false),
                    action = table.Column<string>(type: "varchar", nullable: false),
                    requires_human_actor = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission", x => x.id);
                    table.ForeignKey(
                        name: "FK_permission_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar", nullable: false),
                    code = table.Column<string>(type: "varchar", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_app_user_updated_by",
                        column: x => x.updated_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "security_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    activation_token_lifetime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    lockout_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    max_failed_login_attempts = table.Column<int>(type: "integer", nullable: false),
                    password_history_depth = table.Column<int>(type: "integer", nullable: false),
                    password_min_length = table.Column<int>(type: "integer", nullable: false),
                    password_reset_token_lifetime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    session_absolute_timeout = table.Column<TimeSpan>(type: "interval", nullable: false),
                    session_idle_timeout = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_policy", x => x.id);
                    table.ForeignKey(
                        name: "FK_security_policy_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "varchar", nullable: false),
                    identity_type = table.Column<string>(type: "varchar", nullable: false),
                    identity_provider = table.Column<string>(type: "varchar", nullable: false),
                    subject_id = table.Column<string>(type: "varchar", nullable: false),
                    username = table.Column<string>(type: "varchar", nullable: true),
                    status = table.Column<string>(type: "varchar", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_identity", x => x.id);
                    table.UniqueConstraint("AK_user_identity_id_identity_type", x => new { x.id, x.identity_type });
                    table.ForeignKey(
                        name: "FK_user_identity_app_user_user_id_actor_type",
                        columns: x => new { x.user_id, x.actor_type },
                        principalTable: "app_user",
                        principalColumns: new[] { "id", "actor_type" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permission", x => x.id);
                    table.CheckConstraint("ck_role_permission_revocation_pair", "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_role_permission_app_user_granted_by",
                        column: x => x.granted_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permission_app_user_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permission_permission_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permission_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "varchar", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "text", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_reason = table.Column<string>(type: "text", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role", x => x.id);
                    table.CheckConstraint("ck_user_role_actor_type", "\"actor_type\" IN ('Human', 'Agent', 'System')");
                    table.CheckConstraint("ck_user_role_agent_finite", "\"actor_type\" <> 'Agent' OR \"effective_to\" IS NOT NULL");
                    table.CheckConstraint("ck_user_role_effective_period", "\"effective_to\" IS NULL OR \"effective_to\" > \"effective_from\"");
                    table.CheckConstraint("ck_user_role_revocation_pair", "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");
                    table.CheckConstraint("ck_user_role_revocation_reason", "\"revoked_at\" IS NULL OR \"revocation_reason\" IS NOT NULL");
                    table.CheckConstraint("ck_user_role_scope", "(\"scope_type\" = 'Global' AND \"scope_id\" IS NULL) OR (\"scope_type\" <> 'Global' AND \"scope_id\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_user_role_app_user_assigned_by",
                        column: x => x.assigned_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_app_user_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_app_user_user_id_actor_type",
                        columns: x => new { x.user_id, x.actor_type },
                        principalTable: "app_user",
                        principalColumns: new[] { "id", "actor_type" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_role_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credential",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_type = table.Column<string>(type: "varchar", nullable: false),
                    password_hash = table.Column<string>(type: "varchar", nullable: false),
                    password_algorithm = table.Column<string>(type: "varchar", nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credential", x => x.id);
                    table.ForeignKey(
                        name: "FK_credential_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credential_user_identity_user_identity_id_identity_type",
                        columns: x => new { x.user_identity_id, x.identity_type },
                        principalTable: "user_identity",
                        principalColumns: new[] { "id", "identity_type" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "password_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "varchar", nullable: false),
                    password_algorithm = table.Column<string>(type: "varchar", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_password_history_user_identity_user_identity_id",
                        column: x => x.user_identity_id,
                        principalTable: "user_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_session", x => x.id);
                    table.CheckConstraint("ck_user_session_expires_at", "\"expires_at\" > \"created_at\"");
                    table.CheckConstraint("ck_user_session_last_activity_at", "\"last_activity_at\" >= \"created_at\"");
                    table.CheckConstraint("ck_user_session_revocation_pair", "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");
                    table.CheckConstraint("ck_user_session_revocation_reason", "\"revoked_at\" IS NULL OR \"revocation_reason\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_user_session_app_user_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_session_user_identity_user_identity_id",
                        column: x => x.user_identity_id,
                        principalTable: "user_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_type = table.Column<string>(type: "varchar", nullable: false),
                    token_hash = table.Column<string>(type: "varchar", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_token", x => x.id);
                    table.CheckConstraint("ck_user_token_expires_after_created", "\"expires_at\" > \"created_at\"");
                    table.CheckConstraint("ck_user_token_not_used_and_invalidated", "NOT (\"used_at\" IS NOT NULL AND \"invalidated_at\" IS NOT NULL)");
                    table.CheckConstraint("ck_user_token_token_type", "\"token_type\" IN ('Activation', 'PasswordReset')");
                    table.ForeignKey(
                        name: "FK_user_token_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_token_user_identity_user_identity_id",
                        column: x => x.user_identity_id,
                        principalTable: "user_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_app_user_id_actor_type",
                table: "app_user",
                columns: new[] { "id", "actor_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credential_created_by",
                table: "credential",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_credential_user_identity_id_identity_type",
                table: "credential",
                columns: new[] { "user_identity_id", "identity_type" });

            migrationBuilder.CreateIndex(
                name: "IX_password_history_user_identity_id",
                table: "password_history",
                column: "user_identity_id");

            migrationBuilder.CreateIndex(
                name: "IX_permission_code",
                table: "permission",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permission_created_by",
                table: "permission",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_code",
                table: "role",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_created_by",
                table: "role",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_updated_by",
                table: "role",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_permission_granted_by",
                table: "role_permission",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_permission_permission_id",
                table: "role_permission",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_role_permission_revoked_by",
                table: "role_permission",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_permission_role_id",
                table: "role_permission",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_security_policy_created_by",
                table: "security_policy",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_security_policy_effective_from",
                table: "security_policy",
                column: "effective_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_policy_policy_version",
                table: "security_policy",
                column: "policy_version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_identity_identity_provider_subject_id",
                table: "user_identity",
                columns: new[] { "identity_provider", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_identity_user_id_actor_type",
                table: "user_identity",
                columns: new[] { "user_id", "actor_type" });

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assigned_by",
                table: "user_role",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_revoked_by",
                table: "user_role",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_role_id",
                table: "user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_user_id_actor_type",
                table: "user_role",
                columns: new[] { "user_id", "actor_type" });

            migrationBuilder.CreateIndex(
                name: "IX_user_session_revoked_by",
                table: "user_session",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_session_user_identity_id",
                table: "user_session",
                column: "user_identity_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_token_created_by",
                table: "user_token",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_token_user_identity_id",
                table: "user_token",
                column: "user_identity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credential");

            migrationBuilder.DropTable(
                name: "password_history");

            migrationBuilder.DropTable(
                name: "role_permission");

            migrationBuilder.DropTable(
                name: "security_policy");

            migrationBuilder.DropTable(
                name: "user_role");

            migrationBuilder.DropTable(
                name: "user_session");

            migrationBuilder.DropTable(
                name: "user_token");

            migrationBuilder.DropTable(
                name: "permission");

            migrationBuilder.DropTable(
                name: "role");

            migrationBuilder.DropTable(
                name: "user_identity");

            migrationBuilder.DropTable(
                name: "app_user");
        }
    }
}
