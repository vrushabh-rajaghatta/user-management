using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// The notification table: one row per issued token, recording what happened
    /// when the system tried to deliver the secret.
    ///
    /// An ordinary EF migration in the public schema, owned by migration_role
    /// (N13). Deliberately NOT the audit pattern: the audit schema is owned by a
    /// NOLOGIN role because AUD-S01 proves no reachable role can alter a
    /// committed record. No such proposition is made here — notification rows are
    /// operational state, purged on a schedule — and copying the ownership
    /// boundary would imply a claim this model does not make.
    ///
    /// Three things EF cannot express live below as raw SQL:
    ///
    ///   - the two partial indexes (§3.4), which keep the sweeper and the purge
    ///     job off a full scan and stay small by construction;
    ///   - the N8/N9 update guard and the N10 delete guard. These are the FIRST
    ///     triggers on the public schema — G4's equivalents for User Management
    ///     are still deferred — so they follow the audit schema's established
    ///     shape rather than inventing one: constraint id in the message,
    ///     ERRCODE raise_exception, and ENABLE ALWAYS so the guard survives
    ///     session_replication_role;
    ///   - the grants (N11), including the column-level UPDATE that makes N8
    ///     impossible for app_role rather than merely forbidden.
    ///
    /// No purge_role appears here. Deletion belongs to slice N3, and a role that
    /// nothing yet uses would be a grant with no reader.
    /// </summary>
    public partial class AddNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "varchar", nullable: false),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "varchar", nullable: false),
                    status = table.Column<string>(type: "varchar", nullable: false),
                    not_sent_reason = table.Column<string>(type: "varchar", nullable: true),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transport_message_id = table.Column<string>(type: "varchar", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.id);
                    table.CheckConstraint("ck_notification_attempted_at_shape", "((\"attempted_at\" IS NOT NULL) IS NOT DISTINCT FROM (\"status\" = 'Sent' OR COALESCE(\"not_sent_reason\" IN ('TokenNotLive', 'SubjectInactive', 'TransportFailed'), false)))");
                    table.CheckConstraint("ck_notification_closed_at_iff_terminal", "((\"status\" <> 'Pending') IS NOT DISTINCT FROM (\"closed_at\" IS NOT NULL))");
                    table.CheckConstraint("ck_notification_message_id_only_when_sent", "(\"status\" = 'Sent' OR \"transport_message_id\" IS NULL)");
                    table.CheckConstraint("ck_notification_not_sent_reason", "\"not_sent_reason\" IN ('TokenNotLive', 'SubjectInactive', 'TransportFailed', 'Abandoned')");
                    table.CheckConstraint("ck_notification_reason_iff_not_sent", "((\"status\" = 'NotSent') IS NOT DISTINCT FROM (\"not_sent_reason\" IS NOT NULL))");
                    table.CheckConstraint("ck_notification_status", "\"status\" IN ('Pending', 'Sent', 'NotSent')");
                    table.CheckConstraint("ck_notification_type", "\"notification_type\" IN ('AccountActivation', 'PasswordReset', 'AdminPasswordReset')");
                    table.ForeignKey(
                        name: "FK_notification_user_token_token_id",
                        column: x => x.token_id,
                        principalTable: "user_token",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_notification_token_id",
                table: "notification",
                column: "token_id",
                unique: true);

            // §3.4 — the sweeper's index. Small by construction: a Pending row
            // lives at most the grace window.
            migrationBuilder.Sql(
                """
                CREATE INDEX "ix_notification_pending_created_at"
                ON "notification" ("created_at")
                WHERE "status" = 'Pending';
                """);

            // §3.4 — the purge job's index (slice N3 uses it; the index is part
            // of the table's shape and ships with it).
            migrationBuilder.Sql(
                """
                CREATE INDEX "ix_notification_terminal_closed_at"
                ON "notification" ("closed_at")
                WHERE "status" <> 'Pending';
                """);

            // -------------------------------------------------------------
            // N8 + N9 — one BEFORE UPDATE trigger, two jobs
            //
            // N9 first, because it is the broader refusal: any UPDATE of a row
            // that is no longer Pending is rejected whatever it touches. That
            // is what makes a terminal row write-once, and it is also what
            // makes the sender's terminal write idempotent by refusal — a
            // retry after a write that had in fact succeeded is refused here,
            // and the sender treats the refusal as success.
            // -------------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.notification_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD."status" <> 'Pending' THEN
                        RAISE EXCEPTION
                            'N9: notification % is already terminal (%) and cannot be updated',
                            OLD."id", OLD."status"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF NEW."status" NOT IN ('Sent', 'NotSent') THEN
                        RAISE EXCEPTION
                            'N9: notification % may only leave Pending for Sent or NotSent, not %',
                            OLD."id", NEW."status"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF ROW(OLD."id", OLD."notification_type", OLD."token_id",
                           OLD."recipient", OLD."created_at")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."notification_type", NEW."token_id",
                           NEW."recipient", NEW."created_at") THEN
                        RAISE EXCEPTION
                            'N8: an immutable column of notification % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_notification_update_guard ON "notification";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_notification_update_guard
                    BEFORE UPDATE ON "notification"
                    FOR EACH ROW EXECUTE FUNCTION public.notification_update_guard();
                """);

            // Without ALWAYS, one SET session_replication_role = replica walks
            // straight through the guard.
            migrationBuilder.Sql(
                """
                ALTER TABLE "notification"
                ENABLE ALWAYS TRIGGER tg_notification_update_guard;
                """);

            // -------------------------------------------------------------
            // N10 — a message of unknown fate is never purged, whatever the
            // purge job's predicate says. The predicate is the first control
            // and this is the second; "unreachable" otherwise depends on the
            // predicate being correct.
            // -------------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.notification_reject_pending_delete()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD."status" = 'Pending' THEN
                        RAISE EXCEPTION
                            'N10: notification % is Pending; a message of unknown fate is never deleted',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN OLD;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_notification_reject_pending_delete ON "notification";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_notification_reject_pending_delete
                    BEFORE DELETE ON "notification"
                    FOR EACH ROW EXECUTE FUNCTION public.notification_reject_pending_delete();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "notification"
                ENABLE ALWAYS TRIGGER tg_notification_reject_pending_delete;
                """);

            // -------------------------------------------------------------
            // N11 — app_role reads, inserts, and updates the five lifecycle
            // columns and nothing else. The column list is the point: N8 is
            // then impossible for app_role rather than merely forbidden, and
            // the trigger is the second layer.
            //
            // No DELETE for anyone. provisioning_role gets nothing: it creates
            // the bootstrap administrator's token and writes it to a file, and
            // never sends a notification.
            // -------------------------------------------------------------
            migrationBuilder.Sql(
                """
                REVOKE ALL ON "notification" FROM PUBLIC;
                GRANT SELECT, INSERT ON "notification" TO app_role;
                GRANT UPDATE ("status", "not_sent_reason", "attempted_at", "closed_at", "transport_message_id")
                    ON "notification" TO app_role;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                REVOKE ALL ON "notification" FROM app_role;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_notification_reject_pending_delete ON "notification";
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_notification_update_guard ON "notification";
                """);

            // The table drop would take the triggers with it; the functions are
            // independent objects and would outlive it.
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS public.notification_reject_pending_delete();
                """);

            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS public.notification_update_guard();
                """);

            migrationBuilder.DropTable(
                name: "notification");
        }
    }
}
