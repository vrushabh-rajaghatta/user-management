using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// G4 — the immutability backstop for the eleven User Management tables.
    ///
    /// The frozen model classifies every column of every entity. Two of those
    /// classes are enforcement obligations rather than documentation:
    ///
    ///     Immutable   set once at insert; a BEFORE UPDATE trigger rejects any
    ///                 later change
    ///     Write-once  NULL -> value permitted once; the value can never be
    ///                 changed or cleared afterwards
    ///
    /// Until now neither held at the database. AddUserManagementPrivilegeModel
    /// closed G1's grant half and said so: app_role and provisioning_role hold
    /// UPDATE on every column of every table, with nothing but the domain
    /// layer standing between a defect and a rewritten regulatory record.
    ///
    /// The domain is correct today — UserToken.MarkUsed, UserSession.Revoke
    /// and UserRole.Revoke all refuse a second write, and nothing anywhere
    /// assigns to an immutable property. This migration changes no behaviour.
    /// What it changes is the KIND of claim that can be made: "the database
    /// refuses it" is testable evidence, where "we enforce it in code" is an
    /// assertion about developer discipline that an inspection cannot check.
    ///
    /// 72 immutable columns and 11 write-once columns, across eleven tables.
    ///
    /// Written out per table rather than generated from a column map. The
    /// verbosity is the point: this is invariant infrastructure, and a
    /// reviewer must be able to read what each table actually enforces
    /// without first understanding a generator. The map lives in the tests,
    /// where UserManagementImmutabilityDriftTests holds it against both the
    /// live schema and these function bodies.
    ///
    /// ENABLE ALWAYS on every trigger. Without it a single
    /// SET session_replication_role = replica walks through all eleven.
    ///
    /// ERRCODE raise_exception (P0001) throughout, matching the Notification
    /// guards, so a test can pin the SQLSTATE and notice one layer vanishing
    /// rather than accepting any exception.
    ///
    /// FOUR DEPARTURES from the generic rule, each deliberate:
    ///
    /// 1. AU7 — the System actor row is frozen. app_user's guard rejects ANY
    ///    update to that row, including columns that are Mutable on every
    ///    other row. This is a row-level invariant, not a column
    ///    classification, and it is checked first.
    ///
    /// 2. PH3 and SP2 — password_history and security_policy have no mutable
    ///    column at all, so the generic rule would already refuse every
    ///    substantive update. Their guards refuse EVERY update instead,
    ///    including a no-op. Insert-only and append-only are stronger and
    ///    simpler statements than "each column happens to be immutable", and
    ///    they are the rules the frozen model actually names.
    ///
    /// 3. user_role.effective_to — a documented exception to write-once.
    ///    See the comment above that function.
    ///
    /// 4. role.code is enforced as plainly immutable, although RO2 describes
    ///    immutability conditionally, once the code is referenced. No current
    ///    application operation changes role.code; Role.UpdateMetadata does
    ///    not expose it. Conditional, reference-aware trigger logic — which
    ///    would have to query user_role and the audit snapshots on every
    ///    update — is intentionally not implemented. Recorded as a
    ///    classification deviation rather than a silent simplification.
    ///
    /// NOT in scope, and still outstanding after this migration: PE2
    /// (permission writable only by a release role), G1's user_session purge
    /// exception, and column-level UPDATE grants. Lifecycle-controlled,
    /// Mutable, System-managed and Release-controlled columns are untouched —
    /// G4 does not govern them.
    /// </summary>
    public partial class AddUserManagementImmutabilityTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // =============================================================
            // 1. app_user
            //
            // Immutable: id, actor_type, created_at, created_by
            //
            // AU7 first, and independent of the column classification: the
            // System actor roots every provenance chain in the tenant, so its
            // display name and status are as frozen as its id.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.app_user_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD."actor_type" = 'System' THEN
                        RAISE EXCEPTION
                            'AU7: the System actor row % is frozen; no column may be updated',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF ROW(OLD."id", OLD."actor_type",
                           OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."actor_type",
                           NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of app_user % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_app_user_update_guard ON "app_user";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_app_user_update_guard
                    BEFORE UPDATE ON "app_user"
                    FOR EACH ROW EXECUTE FUNCTION public.app_user_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "app_user" ENABLE ALWAYS TRIGGER tg_app_user_update_guard;
                """);

            // =============================================================
            // 2. user_identity
            //
            // Immutable: id, user_id, actor_type, identity_type,
            //            identity_provider, subject_id, created_at, created_by
            //
            // UI11, and the reason this story exists. subject_id is the
            // provider's permanent identifier for an account; changing it
            // repoints an existing regulatory identity at a different login,
            // which is invariant 5's absolute prohibition and an
            // identity-takeover primitive.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.user_identity_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."user_id", OLD."actor_type",
                           OLD."identity_type", OLD."identity_provider",
                           OLD."subject_id", OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."user_id", NEW."actor_type",
                           NEW."identity_type", NEW."identity_provider",
                           NEW."subject_id", NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of user_identity % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_user_identity_update_guard ON "user_identity";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_user_identity_update_guard
                    BEFORE UPDATE ON "user_identity"
                    FOR EACH ROW EXECUTE FUNCTION public.user_identity_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "user_identity" ENABLE ALWAYS TRIGGER tg_user_identity_update_guard;
                """);

            // =============================================================
            // 3. credential
            //
            // Immutable: id, user_identity_id, identity_type, created_at,
            //            created_by
            //
            // The password columns are Mutable by design — a credential row
            // is rehashed and re-locked constantly. What cannot move is which
            // identity it belongs to.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.credential_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."user_identity_id", OLD."identity_type",
                           OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."user_identity_id", NEW."identity_type",
                           NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of credential % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_credential_update_guard ON "credential";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_credential_update_guard
                    BEFORE UPDATE ON "credential"
                    FOR EACH ROW EXECUTE FUNCTION public.credential_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "credential" ENABLE ALWAYS TRIGGER tg_credential_update_guard;
                """);

            // =============================================================
            // 4. password_history — PH3, insert-only
            //
            // Every one of its five columns is Immutable, so the generic rule
            // would refuse every substantive update. This refuses all of
            // them, no-ops included.
            //
            // A table whose only purpose is preventing password reuse is
            // worthless if its entries can be quietly edited: the policy
            // would weaken with no error, no alert and no visible symptom.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.password_history_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'PH3: password_history is insert-only; row % cannot be updated',
                        OLD."id"
                        USING ERRCODE = 'raise_exception';
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_password_history_update_guard ON "password_history";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_password_history_update_guard
                    BEFORE UPDATE ON "password_history"
                    FOR EACH ROW EXECUTE FUNCTION public.password_history_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "password_history" ENABLE ALWAYS TRIGGER tg_password_history_update_guard;
                """);

            // =============================================================
            // 5. user_token
            //
            // Immutable:  id, user_identity_id, token_type, token_hash,
            //             expires_at, created_at, created_by
            // Write-once: used_at, invalidated_at
            //
            // token_hash is the whole security property: a token is itself a
            // credential, and rewriting the hash of an outstanding token
            // substitutes a secret the issuer never sent.
            //
            // Used and Invalidated are separately write-once because during
            // an investigation they answer different questions — "somebody
            // consumed this" against "nobody ever did". Either being
            // rewritable would erase that distinction after the fact.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.user_token_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."user_identity_id", OLD."token_type",
                           OLD."token_hash", OLD."expires_at",
                           OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."user_identity_id", NEW."token_type",
                           NEW."token_hash", NEW."expires_at",
                           NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of user_token % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."used_at" IS NOT NULL
                       AND NEW."used_at" IS DISTINCT FROM OLD."used_at" THEN
                        RAISE EXCEPTION
                            'G4: user_token.used_at is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."invalidated_at" IS NOT NULL
                       AND NEW."invalidated_at" IS DISTINCT FROM OLD."invalidated_at" THEN
                        RAISE EXCEPTION
                            'G4: user_token.invalidated_at is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_user_token_update_guard ON "user_token";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_user_token_update_guard
                    BEFORE UPDATE ON "user_token"
                    FOR EACH ROW EXECUTE FUNCTION public.user_token_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "user_token" ENABLE ALWAYS TRIGGER tg_user_token_update_guard;
                """);

            // =============================================================
            // 6. user_session
            //
            // Immutable:  id, user_identity_id, created_at, expires_at,
            //             ip_address, user_agent
            // Write-once: revoked_at, revoked_by, revocation_reason
            //
            // last_activity_at is the one mutable column, and deliberately
            // so — it is written on nearly every request.
            //
            // expires_at immutable is what stops a session being silently
            // extended past the absolute timeout the effective policy set.
            // ip_address and user_agent are security evidence: nullable, but
            // not rewritable once captured.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.user_session_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."user_identity_id", OLD."created_at",
                           OLD."expires_at", OLD."ip_address", OLD."user_agent")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."user_identity_id", NEW."created_at",
                           NEW."expires_at", NEW."ip_address", NEW."user_agent") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of user_session % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revoked_at" IS NOT NULL
                       AND NEW."revoked_at" IS DISTINCT FROM OLD."revoked_at" THEN
                        RAISE EXCEPTION
                            'G4: user_session.revoked_at is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revoked_by" IS NOT NULL
                       AND NEW."revoked_by" IS DISTINCT FROM OLD."revoked_by" THEN
                        RAISE EXCEPTION
                            'G4: user_session.revoked_by is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revocation_reason" IS NOT NULL
                       AND NEW."revocation_reason" IS DISTINCT FROM OLD."revocation_reason" THEN
                        RAISE EXCEPTION
                            'G4: user_session.revocation_reason is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_user_session_update_guard ON "user_session";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_user_session_update_guard
                    BEFORE UPDATE ON "user_session"
                    FOR EACH ROW EXECUTE FUNCTION public.user_session_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "user_session" ENABLE ALWAYS TRIGGER tg_user_session_update_guard;
                """);

            // =============================================================
            // 7. security_policy — SP2, append-only
            //
            // All thirteen columns are Immutable: a policy change is a new
            // version, never an edit.
            //
            // The question an auditor asks is "what was the minimum password
            // length when this account was created, in March 2026?". With an
            // editable row that question cannot be answered from the data.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.security_policy_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'SP2: security_policy is append-only; a change is a new version, not an update to %',
                        OLD."id"
                        USING ERRCODE = 'raise_exception';
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_security_policy_update_guard ON "security_policy";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_security_policy_update_guard
                    BEFORE UPDATE ON "security_policy"
                    FOR EACH ROW EXECUTE FUNCTION public.security_policy_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "security_policy" ENABLE ALWAYS TRIGGER tg_security_policy_update_guard;
                """);

            // =============================================================
            // 8. role
            //
            // Immutable: id, code, is_system_role, created_at, created_by
            //
            // name, description and is_active stay mutable: retiring a role
            // is is_active = false, never a delete, because existing
            // assignments and historical audit records still refer to it.
            //
            // code is the stable identifier configuration and code refer to.
            // See departure 4 in the summary above.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.role_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."code", OLD."is_system_role",
                           OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."code", NEW."is_system_role",
                           NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of role % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_role_update_guard ON "role";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_role_update_guard
                    BEFORE UPDATE ON "role"
                    FOR EACH ROW EXECUTE FUNCTION public.role_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "role" ENABLE ALWAYS TRIGGER tg_role_update_guard;
                """);

            // =============================================================
            // 9. permission
            //
            // Immutable: id, code, created_at, created_by
            //
            // The other six columns are Release-controlled, not Mutable: they
            // change by release migration, never by a tenant. That is PE2 — a
            // privilege rule — and it is NOT closed here. G4 governs only the
            // four columns above, so a release may still correct a
            // permission's name or retire it, while its code stays the stable
            // thing application code refers to.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.permission_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."code",
                           OLD."created_at", OLD."created_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."code",
                           NEW."created_at", NEW."created_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of permission % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_permission_update_guard ON "permission";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_permission_update_guard
                    BEFORE UPDATE ON "permission"
                    FOR EACH ROW EXECUTE FUNCTION public.permission_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "permission" ENABLE ALWAYS TRIGGER tg_permission_update_guard;
                """);

            // =============================================================
            // 10. role_permission
            //
            // Immutable:  id, role_id, permission_id, granted_at, granted_by
            // Write-once: revoked_at, revoked_by
            //
            // The pair is immutable and the revocation is write-once, which
            // together are what make the history answer "what did this role
            // actually permit in March 2026?". Repointing role_id or
            // permission_id would rewrite the meaning of a grant that an
            // approval's actor snapshot already cites.
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.role_permission_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."role_id", OLD."permission_id",
                           OLD."granted_at", OLD."granted_by")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."role_id", NEW."permission_id",
                           NEW."granted_at", NEW."granted_by") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of role_permission % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revoked_at" IS NOT NULL
                       AND NEW."revoked_at" IS DISTINCT FROM OLD."revoked_at" THEN
                        RAISE EXCEPTION
                            'G4: role_permission.revoked_at is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revoked_by" IS NOT NULL
                       AND NEW."revoked_by" IS DISTINCT FROM OLD."revoked_by" THEN
                        RAISE EXCEPTION
                            'G4: role_permission.revoked_by is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_role_permission_update_guard ON "role_permission";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_role_permission_update_guard
                    BEFORE UPDATE ON "role_permission"
                    FOR EACH ROW EXECUTE FUNCTION public.role_permission_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "role_permission" ENABLE ALWAYS TRIGGER tg_role_permission_update_guard;
                """);

            // =============================================================
            // 11. user_role
            //
            // Immutable:  id, user_id, actor_type, role_id, scope_type,
            //             scope_id, effective_from, assigned_at, assigned_by,
            //             assignment_reason
            // Write-once: effective_to (see below), revoked_at, revoked_by,
            //             revocation_reason
            //
            // This is the entity the rest of the model exists to support, and
            // it has no status column: the state of an assignment follows
            // entirely from its dates. That makes the dates load-bearing in a
            // way a status column never is — a mutable effective_from would
            // let somebody backdate authority they did not hold.
            //
            // --------------------------------------------------------------
            // effective_to — A DOCUMENTED EXCEPTION TO WRITE-ONCE
            //
            // The workbook classes effective_to as Write-once. The revoked_at
            // row of the same sheet says "Revocation also sets EffectiveTo =
            // now", and UserRole.Revoke does exactly that. For an assignment
            // created WITH an end date — mandatory for agents under UR8,
            // permitted for humans — those two rules collide: revocation must
            // change a non-NULL effective_to.
            //
            // Resolved in favour of the semantic property both rules exist to
            // protect: an authorisation window may CLOSE EARLY, but may never
            // widen or reopen. So:
            //
            //     NULL     -> anything      allowed (ordinary write-once)
            //     non-NULL -> same value    allowed
            //     non-NULL -> NULL          refused  (reopens indefinitely)
            //     non-NULL -> later         refused  (extends authority)
            //     non-NULL -> earlier       allowed ONLY in the same statement
            //                               that sets revoked_at from NULL
            //
            // Consequence worth naming: revoking an assignment whose
            // effective_to has ALREADY PASSED moves the value forward and is
            // refused here. Unreachable today — the only assignment path
            // passes effectiveTo: null — but it constrains the future revoke
            // command, and is recorded in docs/requirements.md rather than
            // worked around in the domain.
            // --------------------------------------------------------------
            // =============================================================
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.user_role_update_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF ROW(OLD."id", OLD."user_id", OLD."actor_type", OLD."role_id",
                           OLD."scope_type", OLD."scope_id", OLD."effective_from",
                           OLD."assigned_at", OLD."assigned_by", OLD."assignment_reason")
                       IS DISTINCT FROM
                       ROW(NEW."id", NEW."user_id", NEW."actor_type", NEW."role_id",
                           NEW."scope_type", NEW."scope_id", NEW."effective_from",
                           NEW."assigned_at", NEW."assigned_by", NEW."assignment_reason") THEN
                        RAISE EXCEPTION
                            'G4: an immutable column of user_role % was changed',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."effective_to" IS NOT NULL
                       AND NEW."effective_to" IS DISTINCT FROM OLD."effective_to" THEN

                        IF NEW."effective_to" IS NULL THEN
                            RAISE EXCEPTION
                                'G4: user_role.effective_to cannot be cleared on %; an assignment window never reopens',
                                OLD."id"
                                USING ERRCODE = 'raise_exception';
                        END IF;

                        IF NEW."effective_to" > OLD."effective_to" THEN
                            RAISE EXCEPTION
                                'G4: user_role.effective_to cannot be extended on %; an assignment window never widens',
                                OLD."id"
                                USING ERRCODE = 'raise_exception';
                        END IF;

                        IF NOT (OLD."revoked_at" IS NULL AND NEW."revoked_at" IS NOT NULL) THEN
                            RAISE EXCEPTION
                                'G4: user_role.effective_to on % may only be closed early by the revocation that sets revoked_at',
                                OLD."id"
                                USING ERRCODE = 'raise_exception';
                        END IF;
                    END IF;

                    IF OLD."revoked_at" IS NOT NULL
                       AND NEW."revoked_at" IS DISTINCT FROM OLD."revoked_at" THEN
                        RAISE EXCEPTION
                            'G4: user_role.revoked_at is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revoked_by" IS NOT NULL
                       AND NEW."revoked_by" IS DISTINCT FROM OLD."revoked_by" THEN
                        RAISE EXCEPTION
                            'G4: user_role.revoked_by is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF OLD."revocation_reason" IS NOT NULL
                       AND NEW."revocation_reason" IS DISTINCT FROM OLD."revocation_reason" THEN
                        RAISE EXCEPTION
                            'G4: user_role.revocation_reason is write-once and already set on %',
                            OLD."id"
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_user_role_update_guard ON "user_role";
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER tg_user_role_update_guard
                    BEFORE UPDATE ON "user_role"
                    FOR EACH ROW EXECUTE FUNCTION public.user_role_update_guard();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "user_role" ENABLE ALWAYS TRIGGER tg_user_role_update_guard;
                """);
        }

        /// <summary>
        /// Triggers first, then the functions they reference. Both are
        /// IF EXISTS so a partially applied Up can still be reversed.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tg_user_role_update_guard ON "user_role";
                DROP TRIGGER IF EXISTS tg_role_permission_update_guard ON "role_permission";
                DROP TRIGGER IF EXISTS tg_permission_update_guard ON "permission";
                DROP TRIGGER IF EXISTS tg_role_update_guard ON "role";
                DROP TRIGGER IF EXISTS tg_security_policy_update_guard ON "security_policy";
                DROP TRIGGER IF EXISTS tg_user_session_update_guard ON "user_session";
                DROP TRIGGER IF EXISTS tg_user_token_update_guard ON "user_token";
                DROP TRIGGER IF EXISTS tg_password_history_update_guard ON "password_history";
                DROP TRIGGER IF EXISTS tg_credential_update_guard ON "credential";
                DROP TRIGGER IF EXISTS tg_user_identity_update_guard ON "user_identity";
                DROP TRIGGER IF EXISTS tg_app_user_update_guard ON "app_user";
                """);

            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS public.user_role_update_guard();
                DROP FUNCTION IF EXISTS public.role_permission_update_guard();
                DROP FUNCTION IF EXISTS public.permission_update_guard();
                DROP FUNCTION IF EXISTS public.role_update_guard();
                DROP FUNCTION IF EXISTS public.security_policy_update_guard();
                DROP FUNCTION IF EXISTS public.user_session_update_guard();
                DROP FUNCTION IF EXISTS public.user_token_update_guard();
                DROP FUNCTION IF EXISTS public.password_history_update_guard();
                DROP FUNCTION IF EXISTS public.credential_update_guard();
                DROP FUNCTION IF EXISTS public.user_identity_update_guard();
                DROP FUNCTION IF EXISTS public.app_user_update_guard();
                """);
        }
    }
}
