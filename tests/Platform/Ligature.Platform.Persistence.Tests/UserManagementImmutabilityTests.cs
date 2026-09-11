using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// G4 — the immutability backstop on the eleven User Management tables,
/// proved by attempting each violation and requiring the refusal.
///
/// These are adversarial tests, not configuration assertions. "The trigger
/// exists" is not the claim; "the attack fails" is. Every attack runs as the
/// PRIVILEGED connection — the owner of these tables, the strongest attacker
/// short of a superuser bypass — because a control that only binds app_role
/// would be a grant, not an invariant.
///
/// Every assertion pins P0001. The rules are layered: a CHECK constraint would
/// also refuse several of these writes, and a test that accepted any exception
/// would stay green while the trigger vanished. BEFORE ROW triggers fire ahead
/// of constraint evaluation, so P0001 is what a live guard produces and 23514
/// or 23503 is what its absence produces.
///
/// FOUR DEPARTURES from the generic rule are tested in their own sections:
/// AU7's row freeze, PH3 and SP2's blanket refusal, and user_role.effective_to's
/// documented write-once exception.
///
/// One database for the class. Every property under test is per-statement, and
/// nothing here attempts DDL, so a shared schema weakens no claim.
/// </summary>
public sealed class UserManagementImmutabilityTests : IAsyncLifetime
{
    private AuditBoundaryDatabase _database = null!;

    /// <summary>The seeded row each table's tests attack, by table name.</summary>
    private readonly Dictionary<string, Guid> _rows = new(StringComparer.Ordinal);

    private static readonly Guid SystemUser =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Guid _subjectUser;
    private Guid _identity;

    // Extra rows for the write-once sections, which need a row already in the
    // terminal state rather than the clean one the immutable tests attack.
    private Guid _usedToken;
    private Guid _invalidatedToken;
    private Guid _revokedSession;
    private Guid _revokedGrant;
    private Guid _boundedAssignment;
    private Guid _revokedAssignment;

    // ------------------------------------------------------------------
    // §1 — every immutable column, one attack each
    //
    // 72 columns. Not a representative sample per table: an off-by-one in a
    // ROW() list is exactly the defect this story exists to prevent, and a
    // column omitted from a guard is invisible to a per-table test.
    // ------------------------------------------------------------------

    [Theory]
    // app_user (4)
    [InlineData("app_user", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("app_user", "actor_type", "'Agent'")]
    [InlineData("app_user", "created_at", "now() - interval '1 day'")]
    [InlineData("app_user", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // user_identity (8) — subject_id is the identity-takeover vector
    [InlineData("user_identity", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_identity", "user_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_identity", "actor_type", "'Agent'")]
    [InlineData("user_identity", "identity_type", "'External'")]
    [InlineData("user_identity", "identity_provider", "'EvilCorp'")]
    [InlineData("user_identity", "subject_id", "'somebody-elses-account'")]
    [InlineData("user_identity", "created_at", "now() - interval '1 day'")]
    [InlineData("user_identity", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // credential (5)
    [InlineData("credential", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("credential", "user_identity_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("credential", "identity_type", "'External'")]
    [InlineData("credential", "created_at", "now() - interval '1 day'")]
    [InlineData("credential", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // password_history (5) — refused by PH3's blanket guard, per column anyway
    [InlineData("password_history", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("password_history", "user_identity_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("password_history", "password_hash", "'a-different-hash'")]
    [InlineData("password_history", "password_algorithm", "'md5'")]
    [InlineData("password_history", "created_at", "now() - interval '1 day'")]
    // user_token (7) — token_hash substitutes a secret the issuer never sent
    [InlineData("user_token", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_token", "user_identity_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_token", "token_type", "'PasswordReset'")]
    [InlineData("user_token", "token_hash", "'an-attacker-chosen-hash'")]
    [InlineData("user_token", "expires_at", "now() + interval '100 days'")]
    [InlineData("user_token", "created_at", "now() - interval '1 day'")]
    [InlineData("user_token", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // user_session (6) — expires_at immutable is what caps the absolute timeout
    [InlineData("user_session", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_session", "user_identity_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_session", "created_at", "now() - interval '1 day'")]
    [InlineData("user_session", "expires_at", "now() + interval '100 days'")]
    [InlineData("user_session", "ip_address", "'10.0.0.9'::inet")]
    [InlineData("user_session", "user_agent", "'a-different-agent'")]
    // security_policy (13) — refused by SP2's blanket guard, per column anyway
    [InlineData("security_policy", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("security_policy", "policy_version", "99")]
    [InlineData("security_policy", "effective_from", "now() - interval '1 day'")]
    [InlineData("security_policy", "created_at", "now() - interval '1 day'")]
    [InlineData("security_policy", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("security_policy", "activation_token_lifetime", "interval '999 hours'")]
    [InlineData("security_policy", "lockout_duration", "interval '1 second'")]
    [InlineData("security_policy", "max_failed_login_attempts", "9999")]
    [InlineData("security_policy", "password_history_depth", "0")]
    [InlineData("security_policy", "password_min_length", "4")]
    [InlineData("security_policy", "password_reset_token_lifetime", "interval '999 hours'")]
    [InlineData("security_policy", "session_absolute_timeout", "interval '999 hours'")]
    [InlineData("security_policy", "session_idle_timeout", "interval '999 hours'")]
    // role (5)
    [InlineData("role", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("role", "code", "'A_DIFFERENT_CODE'")]
    [InlineData("role", "is_system_role", "true")]
    [InlineData("role", "created_at", "now() - interval '1 day'")]
    [InlineData("role", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // permission (4)
    [InlineData("permission", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("permission", "code", "'a.different.code'")]
    [InlineData("permission", "created_at", "now() - interval '1 day'")]
    [InlineData("permission", "created_by", "'99999999-9999-9999-9999-999999999999'")]
    // role_permission (5)
    [InlineData("role_permission", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("role_permission", "role_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("role_permission", "permission_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("role_permission", "granted_at", "now() - interval '1 day'")]
    [InlineData("role_permission", "granted_by", "'99999999-9999-9999-9999-999999999999'")]
    // user_role (10) — no status column, so the dates carry all the authority
    [InlineData("user_role", "id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "user_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "actor_type", "'Agent'")]
    [InlineData("user_role", "role_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "scope_type", "'Product'")]
    [InlineData("user_role", "scope_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "effective_from", "now() - interval '100 days'")]
    [InlineData("user_role", "assigned_at", "now() - interval '1 day'")]
    [InlineData("user_role", "assigned_by", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "assignment_reason", "'a rewritten justification'")]
    public async Task An_immutable_column_cannot_be_changed(
        string table, string column, string newValue)
    {
        var failure = await RefusedAsync(
            $"""UPDATE "{table}" SET "{column}" = {newValue} WHERE "id" = '{_rows[table]}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    /// <summary>
    /// The generic guard compares ROW(OLD…) IS DISTINCT FROM ROW(NEW…), so a
    /// write that names an immutable column without changing its value is not
    /// a change and must pass. Retained deliberately: it is the behaviour that
    /// separates ordinary G4 from PH3/SP2, which refuse their no-ops too.
    /// </summary>
    [Theory]
    [InlineData("app_user", "actor_type")]
    [InlineData("user_identity", "subject_id")]
    [InlineData("credential", "user_identity_id")]
    [InlineData("user_token", "token_hash")]
    [InlineData("user_session", "expires_at")]
    [InlineData("role", "code")]
    [InlineData("permission", "code")]
    [InlineData("role_permission", "role_id")]
    [InlineData("user_role", "effective_from")]
    public async Task A_no_op_write_to_an_immutable_column_is_permitted(
        string table, string column)
        => await ExecuteAsync(
            $"""UPDATE "{table}" SET "{column}" = "{column}" WHERE "id" = '{_rows[table]}'""");

    /// <summary>
    /// The guards must not over-refuse. Each table below has at least one
    /// genuinely mutable column, and the application writes to it constantly.
    /// </summary>
    [Theory]
    [InlineData("app_user", "display_name", "'A New Display Name'")]
    [InlineData("app_user", "status", "'Inactive'")]
    [InlineData("user_identity", "username", "'a-new-username'")]
    [InlineData("credential", "password_hash", "'a-rehashed-value'")]
    [InlineData("credential", "failed_attempt_count", "3")]
    [InlineData("user_session", "last_activity_at", "now()")]
    [InlineData("role", "name", "'A Renamed Role'")]
    [InlineData("role", "is_active", "false")]
    [InlineData("permission", "is_active", "false")]
    public async Task A_mutable_column_is_still_writable(
        string table, string column, string newValue)
        => await ExecuteAsync(
            $"""UPDATE "{table}" SET "{column}" = {newValue} WHERE "id" = '{_rows[table]}'""");

    // ------------------------------------------------------------------
    // §2 — write-once: NULL -> value once, and never again
    //
    // user_role.effective_to is excluded here and tested in §4, because it
    // carries a documented exception the generic rule does not.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("user_token", "used_at", "now()")]
    [InlineData("user_token", "invalidated_at", "now()")]
    [InlineData("user_session", "revoked_at", "now()")]
    [InlineData("role_permission", "revoked_at", "now()")]
    public async Task A_write_once_column_accepts_its_first_value(
        string table, string column, string value)
    {
        // Each case seeds its OWN row rather than sharing the class fixture's.
        // Two reasons, both order-independence: this test consumes the
        // transition it is testing, and ck_user_token_not_used_and_invalidated
        // forbids one token holding both of user_token's write-once columns.
        var id = table switch
        {
            "user_token" => await SeedTokenAsync(used: false, invalidated: false),
            "user_session" => await SeedSessionAsync(revoked: false),
            // Its own permission too: the partial unique index on
            // (role_id, permission_id) covers unrevoked rows, so a second live
            // grant of the same pair would collide with the class fixture's.
            "role_permission" => await SeedGrantAsync(
                revoked: false, permissionId: await SeedPermissionAsync()),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        // The pair CHECKs mean the companion columns travel with the first
        // value — a revocation is a trio, not a timestamp.
        var companions = (table, column) switch
        {
            ("user_session", "revoked_at")
                => $""", "revoked_by" = '{SystemUser}', "revocation_reason" = 'Logout'""",
            ("role_permission", "revoked_at")
                => $""", "revoked_by" = '{SystemUser}'""",
            _ => string.Empty,
        };

        await ExecuteAsync(
            $"""
             UPDATE "{table}" SET "{column}" = {value}{companions} WHERE "id" = '{id}'
             """);
    }

    [Theory]
    [InlineData("user_token", "used_at")]
    [InlineData("user_token", "invalidated_at")]
    [InlineData("user_session", "revoked_at")]
    [InlineData("user_session", "revoked_by")]
    [InlineData("user_session", "revocation_reason")]
    [InlineData("role_permission", "revoked_at")]
    [InlineData("role_permission", "revoked_by")]
    [InlineData("user_role", "revoked_at")]
    [InlineData("user_role", "revoked_by")]
    [InlineData("user_role", "revocation_reason")]
    public async Task A_write_once_column_cannot_be_changed_once_set(
        string table, string column)
    {
        var (id, replacement) = TerminalRow(table, column);

        var failure = await RefusedAsync(
            $"""UPDATE "{table}" SET "{column}" = {replacement} WHERE "id" = '{id}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Theory]
    [InlineData("user_token", "used_at")]
    [InlineData("user_token", "invalidated_at")]
    [InlineData("user_session", "revoked_at")]
    [InlineData("user_session", "revoked_by")]
    [InlineData("user_session", "revocation_reason")]
    [InlineData("role_permission", "revoked_at")]
    [InlineData("role_permission", "revoked_by")]
    [InlineData("user_role", "revoked_at")]
    [InlineData("user_role", "revoked_by")]
    [InlineData("user_role", "revocation_reason")]
    public async Task A_write_once_column_cannot_be_cleared(
        string table, string column)
    {
        // Clearing a revocation is how a terminated session or assignment
        // would come back to life. Several of these would also break a pair
        // CHECK — the trigger fires first, which is what the SQLSTATE proves.
        var (id, _) = TerminalRow(table, column);

        var failure = await RefusedAsync(
            $"""UPDATE "{table}" SET "{column}" = NULL WHERE "id" = '{id}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Theory]
    [InlineData("user_token", "used_at")]
    [InlineData("user_token", "invalidated_at")]
    [InlineData("user_session", "revoked_at")]
    [InlineData("role_permission", "revoked_at")]
    [InlineData("user_role", "revoked_at")]
    public async Task A_write_once_column_tolerates_a_write_of_the_same_value(
        string table, string column)
    {
        // EF sends only modified columns, so an untouched write-once column
        // arrives with NEW = OLD. Refusing that would break every ordinary
        // update to a row that had already reached its terminal state.
        var (id, _) = TerminalRow(table, column);

        await ExecuteAsync(
            $"""UPDATE "{table}" SET "{column}" = "{column}" WHERE "id" = '{id}'""");
    }

    // ------------------------------------------------------------------
    // §3 — AU7: the System actor row is frozen
    //
    // A row-level invariant, not a column classification. The System actor
    // roots every provenance chain in the tenant, so its display name is as
    // frozen as its id — and that is why it is tested separately rather than
    // inferred from §1.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("display_name", "'Renamed System'")]
    [InlineData("status", "'Inactive'")]
    [InlineData("email", "'system@example.test'")]
    [InlineData("updated_at", "now()")]
    public async Task No_column_of_the_System_actor_row_can_be_updated(
        string column, string value)
    {
        // Every one of these is Mutable or System-managed on an ordinary row
        // and accepted by A_mutable_column_is_still_writable above.
        var failure = await RefusedAsync(
            $"""UPDATE "app_user" SET "{column}" = {value} WHERE "id" = '{SystemUser}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task The_same_column_is_writable_on_an_ordinary_row()
        => await ExecuteAsync(
            $"""UPDATE "app_user" SET "display_name" = 'Ordinary' WHERE "id" = '{_subjectUser}'""");

    [Fact]
    public async Task A_no_op_update_of_the_System_actor_row_is_still_refused()
    {
        // AU7 is checked before the column comparison, so unlike ordinary G4
        // the System row refuses even a write that changes nothing.
        var failure = await RefusedAsync(
            $"""UPDATE "app_user" SET "display_name" = "display_name" WHERE "id" = '{SystemUser}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // §4 — user_role.effective_to, the documented write-once exception
    //
    // An authorisation window may close early. It may never widen or reopen.
    // ------------------------------------------------------------------

    /// <summary>
    /// Every test in this section seeds its OWN assignment. The successful
    /// revocation below consumes the exception it proves — a shared row would
    /// leave the refusals after it passing for the wrong reason, because once
    /// revoked_at is set every effective_to change is refused by a different
    /// branch than the one under test.
    /// </summary>
    [Fact]
    public async Task An_open_ended_assignment_may_be_given_an_end_date()
        => await ExecuteAsync(
            $"""
             UPDATE "user_role" SET "effective_to" = now() + interval '10 days'
             WHERE "id" = '{await SeedBoundedAssignmentAsync(effectiveTo: "NULL")}'
             """);

    [Fact]
    public async Task A_bounded_assignment_cannot_have_its_end_date_extended()
    {
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role" SET "effective_to" = now() + interval '90 days'
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task A_bounded_assignment_cannot_have_its_end_date_cleared()
    {
        // Clearing it would reopen the window indefinitely — the widest
        // possible extension, expressed as an absence.
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role" SET "effective_to" = NULL
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task A_bounded_assignment_cannot_be_closed_early_without_a_revocation()
    {
        // Narrowing the window silently is still a rewrite of the record: it
        // would leave no revoking actor, no time and no reason.
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role" SET "effective_to" = now() + interval '1 day'
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task A_revocation_may_close_a_bounded_assignment_early()
        // The whole exception, in one statement: effective_to moves earlier in
        // the same UPDATE that sets revoked_at from NULL.
        => await ExecuteAsync(
            $"""
             UPDATE "user_role"
             SET "effective_to" = now() + interval '1 day',
                 "revoked_at" = now(),
                 "revoked_by" = '{SystemUser}',
                 "revocation_reason" = 'Access review'
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

    /// <summary>
    /// The exception permits a revocation to move effective_to EARLIER. These
    /// two prove it permits nothing else in the same breath.
    ///
    /// Found by mutation: deleting the "never widens" branch left every other
    /// test in this file green, because an extension without a revocation is
    /// already caught by the branch below it. Only an extension carried INSIDE
    /// a revocation distinguishes them — and that is the dangerous shape, since
    /// it is the one statement the trigger is obliged to let through.
    /// </summary>
    [Fact]
    public async Task A_revocation_cannot_extend_the_window_it_closes()
    {
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role"
             SET "effective_to" = now() + interval '90 days',
                 "revoked_at" = now(),
                 "revoked_by" = '{SystemUser}',
                 "revocation_reason" = 'Access review'
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task A_revocation_cannot_clear_the_window_it_closes()
    {
        // Clearing during revocation is the same attack expressed as an
        // absence: a revoked assignment whose end date is open-ended.
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role"
             SET "effective_to" = NULL,
                 "revoked_at" = now(),
                 "revoked_by" = '{SystemUser}',
                 "revocation_reason" = 'Access review'
             WHERE "id" = '{await SeedBoundedAssignmentAsync()}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task An_already_revoked_assignment_cannot_be_closed_earlier_again()
    {
        // The exception is spent once revoked_at is set: OLD.revoked_at is no
        // longer NULL, so the early-closure branch is unreachable.
        var failure = await RefusedAsync(
            $"""
             UPDATE "user_role" SET "effective_to" = now() + interval '1 hour'
             WHERE "id" = '{_revokedAssignment}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    /// <summary>
    /// A live assignment ending in 30 days, on a role of its own — UR5 and UR6
    /// forbid two assignments overlapping for the same user, role and scope.
    /// </summary>
    private async Task<Guid> SeedBoundedAssignmentAsync(
        string effectiveTo = "now() + interval '30 days'")
        => await SeedAssignmentAsync(
            await SeedRoleAsync(), effectiveTo, revoked: false);

    // ------------------------------------------------------------------
    // §5 — PH3 and SP2: insert-only and append-only
    //
    // Stronger than the generic rule, deliberately. Every column of both
    // tables is Immutable, so G4 alone would refuse every substantive write;
    // these guards refuse the no-op too, which is what "insert-only" and
    // "append-only" actually mean.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Password_history_refuses_an_update_that_changes_nothing()
    {
        var failure = await RefusedAsync(
            $"""
             UPDATE "password_history" SET "password_hash" = "password_hash"
             WHERE "id" = '{_rows["password_history"]}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    [Fact]
    public async Task Security_policy_refuses_an_update_that_changes_nothing()
    {
        var failure = await RefusedAsync(
            $"""
             UPDATE "security_policy" SET "password_min_length" = "password_min_length"
             WHERE "id" = '{_rows["security_policy"]}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // §6 — the bypass. ENABLE ALWAYS is what makes these fail.
    //
    // Without it, one SET session_replication_role = replica walks through
    // every guard in this file.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("app_user", "actor_type", "'Agent'")]
    [InlineData("user_identity", "subject_id", "'somebody-elses-account'")]
    [InlineData("credential", "user_identity_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("password_history", "password_hash", "'a-different-hash'")]
    [InlineData("user_token", "token_hash", "'an-attacker-chosen-hash'")]
    [InlineData("user_session", "expires_at", "now() + interval '100 days'")]
    [InlineData("security_policy", "password_min_length", "4")]
    [InlineData("role", "code", "'A_DIFFERENT_CODE'")]
    [InlineData("permission", "code", "'a.different.code'")]
    [InlineData("role_permission", "role_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("user_role", "effective_from", "now() - interval '100 days'")]
    public async Task Session_replication_role_does_not_disable_the_guards(
        string table, string column, string newValue)
    {
        var failure = await RefusedAsync(
            $"""
             SET session_replication_role = replica;
             UPDATE "{table}" SET "{column}" = {newValue} WHERE "id" = '{_rows[table]}';
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// The row already in its terminal state for this column, and a value
    /// different from the one it holds.
    /// </summary>
    private (Guid Id, string Replacement) TerminalRow(string table, string column)
        => (table, column) switch
        {
            ("user_token", "used_at") => (_usedToken, "now() + interval '1 hour'"),
            ("user_token", "invalidated_at") => (_invalidatedToken, "now() + interval '1 hour'"),
            ("user_session", "revoked_at") => (_revokedSession, "now() + interval '1 hour'"),
            ("user_session", "revoked_by") => (_revokedSession, $"'{_subjectUser}'"),
            ("user_session", "revocation_reason") => (_revokedSession, "'AdminRevoked'"),
            ("role_permission", "revoked_at") => (_revokedGrant, "now() + interval '1 hour'"),
            ("role_permission", "revoked_by") => (_revokedGrant, $"'{_subjectUser}'"),
            ("user_role", "revoked_at") => (_revokedAssignment, "now() + interval '1 hour'"),
            ("user_role", "revoked_by") => (_revokedAssignment, $"'{_subjectUser}'"),
            ("user_role", "revocation_reason") => (_revokedAssignment, "'A rewritten reason'"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(column), $"no terminal row seeded for {table}.{column}"),
        };

    private async Task<PostgresException> RefusedAsync(string sql)
        => await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

    private async Task ExecuteAsync(string sql)
    {
        await using var connection =
            new NpgsqlConnection(_database.PrivilegedConnection);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------- seeding

    public async Task InitializeAsync()
    {
        _database = await AuditBoundaryDatabase.CreateAsync();

        _subjectUser = Guid.NewGuid();
        _identity = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "app_user"
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{SystemUser}', 'System', NULL, NULL, 'System', NULL, 'Active',
                  now(), '{SystemUser}', now(), '{SystemUser}'),
                 ('{_subjectUser}', 'Human', 'Jo', 'Smith', 'Jo Smith',
                  'jo-{Guid.NewGuid():N}@example.test', 'Active',
                  now(), '{SystemUser}', now(), '{SystemUser}');

             INSERT INTO "user_identity"
                 (id, user_id, actor_type, identity_type, identity_provider, subject_id,
                  username, status, created_at, created_by)
             VALUES
                 ('{_identity}', '{_subjectUser}', 'Human', 'Local', 'Application',
                  '{_identity}', 'jo-{Guid.NewGuid():N}', 'Active', now(), '{SystemUser}');
             """);

        _rows["app_user"] = _subjectUser;
        _rows["user_identity"] = _identity;

        _rows["credential"] = await SeedCredentialAsync();
        _rows["password_history"] = await SeedPasswordHistoryAsync();

        _rows["user_token"] = await SeedTokenAsync(used: false, invalidated: false);
        _usedToken = await SeedTokenAsync(used: true, invalidated: false);
        _invalidatedToken = await SeedTokenAsync(used: false, invalidated: true);

        _rows["user_session"] = await SeedSessionAsync(revoked: false);
        _revokedSession = await SeedSessionAsync(revoked: true);

        _rows["security_policy"] = await SeedSecurityPolicyAsync();
        _rows["role"] = await SeedRoleAsync();
        _rows["permission"] = await SeedPermissionAsync();

        _rows["role_permission"] = await SeedGrantAsync(revoked: false);
        _revokedGrant = await SeedGrantAsync(revoked: true);

        // UR5 and UR6 forbid overlapping assignments for the same user, role
        // and scope, so each assignment below gets its own role. Seeding an
        // extra role is cheaper than reasoning about non-overlapping windows.
        //
        // Open-ended: effective_to is NULL, so §4's first-value case applies.
        _rows["user_role"] = await SeedAssignmentAsync(
            await SeedRoleAsync(), effectiveTo: "NULL", revoked: false);

        // Bounded and live: the row the effective_to exception is about.
        _boundedAssignment = await SeedAssignmentAsync(
            await SeedRoleAsync(),
            effectiveTo: "now() + interval '30 days'",
            revoked: false);

        _revokedAssignment = await SeedAssignmentAsync(
            await SeedRoleAsync(),
            effectiveTo: "now() + interval '30 days'",
            revoked: true);
    }

    private async Task<Guid> SeedCredentialAsync()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "credential"
                 (id, user_identity_id, identity_type, password_hash, password_algorithm,
                  password_changed_at, must_change_password, failed_attempt_count,
                  locked_until, created_at, created_by)
             VALUES
                 ('{id}', '{_identity}', 'Local', 'a-hash', 'argon2id',
                  now(), false, 0, NULL, now(), '{SystemUser}')
             """);

        return id;
    }

    private async Task<Guid> SeedPasswordHistoryAsync()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "password_history"
                 (id, user_identity_id, password_hash, password_algorithm, created_at)
             VALUES
                 ('{id}', '{_identity}', 'a-superseded-hash', 'argon2id', now())
             """);

        return id;
    }

    private async Task<Guid> SeedTokenAsync(bool used, bool invalidated)
    {
        var id = Guid.NewGuid();

        // ck_user_token_not_used_and_invalidated forbids both at once, which
        // is why the two write-once columns need separate rows.
        await ExecuteAsync(
            $"""
             INSERT INTO "user_token"
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{id}', '{_identity}', 'Activation', 'hash-{Guid.NewGuid():N}',
                  now() + interval '72 hours',
                  {(used ? "now()" : "NULL")}, {(invalidated ? "now()" : "NULL")},
                  now(), '{SystemUser}')
             """);

        return id;
    }

    private async Task<Guid> SeedSessionAsync(bool revoked)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "user_session"
                 (id, user_identity_id, created_at, last_activity_at, expires_at,
                  revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
             VALUES
                 ('{id}', '{_identity}', now(), now(), now() + interval '12 hours',
                  {(revoked ? "now()" : "NULL")},
                  {(revoked ? $"'{SystemUser}'" : "NULL")},
                  {(revoked ? "'Logout'" : "NULL")},
                  '10.0.0.1'::inet, 'a-user-agent')
             """);

        return id;
    }

    private async Task<Guid> SeedSecurityPolicyAsync()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "security_policy"
                 (id, policy_version, effective_from, created_at, created_by,
                  activation_token_lifetime, lockout_duration, max_failed_login_attempts,
                  password_history_depth, password_min_length, password_reset_token_lifetime,
                  session_absolute_timeout, session_idle_timeout)
             VALUES
                 ('{id}', 1, now(), now(), '{SystemUser}',
                  interval '72 hours', interval '15 minutes', 5, 5, 12,
                  interval '1 hour', interval '12 hours', interval '15 minutes')
             """);

        return id;
    }

    private async Task<Guid> SeedRoleAsync()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "role"
                 (id, name, code, description, is_system_role, is_active,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{id}', 'A Role', 'ROLE_{Guid.NewGuid():N}', NULL, false, true,
                  now(), '{SystemUser}', now(), '{SystemUser}')
             """);

        return id;
    }

    private async Task<Guid> SeedPermissionAsync()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "permission"
                 (id, code, name, description, resource, action,
                  requires_human_actor, is_active, created_at, created_by)
             VALUES
                 ('{id}', 'perm.{Guid.NewGuid():N}', 'A Permission', NULL, 'Thing', 'Do',
                  false, true, now(), '{SystemUser}')
             """);

        return id;
    }

    private async Task<Guid> SeedGrantAsync(bool revoked, Guid? permissionId = null)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "role_permission"
                 (id, role_id, permission_id, granted_at, granted_by, revoked_at, revoked_by)
             VALUES
                 ('{id}', '{_rows["role"]}', '{permissionId ?? _rows["permission"]}', now(), '{SystemUser}',
                  {(revoked ? "now()" : "NULL")},
                  {(revoked ? $"'{SystemUser}'" : "NULL")})
             """);

        return id;
    }

    private async Task<Guid> SeedAssignmentAsync(
        Guid roleId, string effectiveTo, bool revoked)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO "user_role"
                 (id, user_id, actor_type, role_id, scope_type, scope_id,
                  effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                  revoked_at, revoked_by, revocation_reason)
             VALUES
                 ('{id}', '{_subjectUser}', 'Human', '{roleId}', 'Global', NULL,
                  now() - interval '1 day', {effectiveTo}, now(), '{SystemUser}', 'Seeded',
                  {(revoked ? "now()" : "NULL")},
                  {(revoked ? $"'{SystemUser}'" : "NULL")},
                  {(revoked ? "'Seeded revocation'" : "NULL")})
             """);

        return id;
    }

    public async Task DisposeAsync()
        => await _database.DisposeAsync();
}
