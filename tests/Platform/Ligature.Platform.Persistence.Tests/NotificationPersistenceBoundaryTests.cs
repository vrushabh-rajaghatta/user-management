using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Phase A of Notification slice N1 — the persistence controls, proved by
/// attempting each violation and requiring the refusal.
///
/// These are adversarial tests, not configuration assertions. "The grants are
/// as expected" is not the claim; "the attack fails" is. Every negative test
/// asserts the SPECIFIC SQLSTATE, because the rules are layered and a test that
/// accepted any exception would not notice one layer disappearing:
///
///     23514  CHECK          N3–N7, the six legal row shapes
///     23505  unique         N2, one notification per issued token
///     23503  foreign key    N2, the token must exist
///     42501  privilege      N11, what app_role may and may not touch
///     P0001  trigger        N8/N9/N10, the lifecycle guards
///
/// If the column-level UPDATE grant were dropped, the N8 trigger would still
/// refuse the write — and an N11 test that only required "some exception" would
/// stay green while a control vanished. Hence the SQLSTATE on every assertion.
///
/// One database for the class rather than one per test. The property under test
/// is per-statement, and nothing here attempts DDL, so a shared schema does not
/// weaken any claim; each test uses its own token and notification ids.
/// </summary>
public sealed class NotificationPersistenceBoundaryTests : IAsyncLifetime
{
    private AuditBoundaryDatabase _database = null!;

    public async Task InitializeAsync()
        => _database = await AuditBoundaryDatabase.CreateAsync();

    public async Task DisposeAsync()
        => await _database.DisposeAsync();

    // ------------------------------------------------------------------
    // §4.1 — the six legal row shapes, and nothing else
    // ------------------------------------------------------------------

    [Fact]
    public async Task All_six_legal_row_shapes_are_accepted()
    {
        // Proves the CHECKs do not over-refuse. Every shape below is reachable
        // by exactly one path in the state model, and each needs its own token
        // because of N2.
        await InsertAsync(Privileged, await Shape(status: "Pending"));

        await InsertAsync(Privileged, await Shape(
            status: "Sent", attempted: true, closed: true,
            transportMessageId: "provider-ref-1"));

        // Sent with no acceptance reference: N7 permits it, because not every
        // transport returns one.
        await InsertAsync(Privileged, await Shape(
            status: "Sent", attempted: true, closed: true));

        foreach (var reason in new[] { "TokenNotLive", "SubjectInactive", "TransportFailed" })
        {
            await InsertAsync(Privileged, await Shape(
                status: "NotSent", reason: reason, attempted: true, closed: true));
        }

        // Abandoned is the only terminal shape without an attempt time.
        await InsertAsync(Privileged, await Shape(
            status: "NotSent", reason: "Abandoned", closed: true));
    }

    [Fact]
    public async Task Created_at_is_defaulted_by_the_database()
    {
        var id = Guid.NewGuid();

        // The INSERT does not name created_at at all, so the column being
        // populated is already evidence of a default. What this asserts beyond
        // that is that the default is now() rather than some fixed literal:
        // the sweeper compares this value against the database's own now(),
        // and a constant default would make every row eligible forever.
        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var isDatabaseNow = await ScalarAsync<bool>(
            Privileged,
            $"""
             SELECT "created_at" BETWEEN now() - interval '1 minute' AND now()
             FROM "notification" WHERE "id" = '{id}'
             """);

        Assert.True(isDatabaseNow);
    }

    // ------------------------------------------------------------------
    // N2 — at most one notification per issued token
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_second_notification_for_one_token_is_refused()
    {
        var tokenId = await SeedTokenAsync();

        await InsertAsync(Privileged, Insert(Guid.NewGuid(), tokenId));

        var failure = await Refused(Privileged, Insert(Guid.NewGuid(), tokenId));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }

    [Fact]
    public async Task A_notification_for_a_token_that_does_not_exist_is_refused()
    {
        var failure = await Refused(
            Privileged, Insert(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // N3 — the release-controlled sets
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Digest", "Pending", null)]
    [InlineData("AccountActivation", "Delivered", null)]
    [InlineData("AccountActivation", "NotSent", "MailboxFull")]
    public async Task An_undeclared_enum_value_is_refused(
        string type, string status, string? reason)
    {
        var failure = await Refused(Privileged, await Shape(
            type: type,
            status: status,
            reason: reason,
            attempted: status == "NotSent",
            closed: status != "Pending"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // N4–N7 — the shape rules
    // ------------------------------------------------------------------

    [Theory]
    // N4 — a reason is present if and only if the row is NotSent
    [InlineData("NotSent", null, true, true, null)]
    [InlineData("Sent", "TransportFailed", true, true, null)]
    [InlineData("Pending", "TransportFailed", false, false, null)]
    // N5 — closed_at is present if and only if the row is terminal
    [InlineData("Pending", null, false, true, null)]
    [InlineData("Sent", null, true, false, null)]
    // N6 — every terminal row except Abandoned carries an attempt time
    [InlineData("Sent", null, false, true, null)]
    [InlineData("NotSent", "Abandoned", true, true, null)]
    [InlineData("NotSent", "TransportFailed", false, true, null)]
    [InlineData("Pending", null, true, false, null)]
    // N7 — an acceptance reference only ever accompanies Sent
    [InlineData("NotSent", "TransportFailed", true, true, "provider-ref")]
    [InlineData("Pending", null, false, false, "provider-ref")]
    public async Task An_illegal_row_shape_is_refused(
        string status,
        string? reason,
        bool attempted,
        bool closed,
        string? transportMessageId)
    {
        var failure = await Refused(Privileged, await Shape(
            status: status,
            reason: reason,
            attempted: attempted,
            closed: closed,
            transportMessageId: transportMessageId));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // N8 — the immutable set, attempted as a SUPERUSER
    //
    // Deliberately not as app_role: the column grant would refuse these before
    // the trigger saw them, and then the test would prove the grant twice and
    // the trigger never. The superuser walks through the grant and lands on the
    // guard, which is the layer under test here.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("recipient", "'someone.else@example.com'")]
    [InlineData("notification_type", "'PasswordReset'")]
    [InlineData("created_at", "now()")]
    public async Task An_immutable_column_cannot_be_changed(
        string column, string value)
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var failure = await Refused(
            Privileged,
            $"""
             UPDATE "notification"
             SET "status" = 'Sent', "attempted_at" = now(), "closed_at" = now(),
                 "{column}" = {value}
             WHERE "id" = '{id}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N8", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_a_notification_refers_to_cannot_be_changed()
    {
        var id = Guid.NewGuid();
        var other = await SeedTokenAsync();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var failure = await Refused(
            Privileged,
            $"""
             UPDATE "notification"
             SET "status" = 'Sent', "attempted_at" = now(), "closed_at" = now(),
                 "token_id" = '{other}'
             WHERE "id" = '{id}'
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N8", failure.MessageText, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // N9 — one transition, out of Pending, once
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_pending_row_can_be_closed_exactly_once()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var affected = await ExecuteAsync(Privileged, CloseAsSent(id));

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task A_terminal_row_cannot_be_rewritten()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(
            id: id, status: "Sent", attempted: true, closed: true));

        var failure = await Refused(
            Privileged,
            $"""UPDATE "notification" SET "closed_at" = now() WHERE "id" = '{id}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N9", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_terminal_row_cannot_move_to_another_terminal_state()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(
            id: id, status: "NotSent", reason: "TransportFailed",
            attempted: true, closed: true));

        var failure = await Refused(Privileged, CloseAsSent(id));

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N9", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pending_row_cannot_be_updated_and_left_pending()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var failure = await Refused(
            Privileged,
            $"""UPDATE "notification" SET "status" = 'Pending' WHERE "id" = '{id}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N9", failure.MessageText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal the sender's single terminal-write retry depends on: if the
    /// first write had in fact committed, the second is refused here and the
    /// sender treats that refusal as success. The row must be unchanged.
    /// </summary>
    [Fact]
    public async Task A_repeated_terminal_write_is_refused_and_changes_nothing()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));
        await ExecuteAsync(Privileged, CloseAsSent(id, "provider-ref-1"));

        var failure = await Refused(Privileged, CloseAsSent(id, "provider-ref-2"));

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);

        var reference = await ScalarAsync<string>(
            Privileged,
            $"""SELECT "transport_message_id" FROM "notification" WHERE "id" = '{id}'""");

        Assert.Equal("provider-ref-1", reference);
    }

    // ------------------------------------------------------------------
    // N10 — a message of unknown fate is never deleted
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_pending_row_cannot_be_deleted()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(id: id, status: "Pending"));

        var failure = await Refused(
            Privileged, $"""DELETE FROM "notification" WHERE "id" = '{id}'""");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Contains("N10", failure.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_terminal_row_can_be_deleted()
    {
        // Proves N10 refuses the Pending row specifically, rather than refusing
        // deletion outright — slice N3's purge depends on this being possible.
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(
            id: id, status: "NotSent", reason: "Abandoned", closed: true));

        var affected = await ExecuteAsync(
            Privileged, $"""DELETE FROM "notification" WHERE "id" = '{id}'""");

        Assert.Equal(1, affected);
    }

    // ------------------------------------------------------------------
    // The bypass. ENABLE ALWAYS is what makes this fail.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Session_replication_role_does_not_disable_the_guards()
    {
        var id = Guid.NewGuid();

        await InsertAsync(Privileged, await Shape(
            id: id, status: "Sent", attempted: true, closed: true));

        var failure = await Refused(
            Privileged,
            $"""
             SET session_replication_role = replica;
             UPDATE "notification" SET "recipient" = 'elsewhere@example.com'
             WHERE "id" = '{id}';
             """);

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // N11 — what the running application may and may not do
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_application_can_insert_and_read_a_pending_row()
    {
        var id = Guid.NewGuid();

        await InsertAsync(AppRole, await Shape(id: id, status: "Pending"));

        var status = await ScalarAsync<string>(
            AppRole, $"""SELECT "status" FROM "notification" WHERE "id" = '{id}'""");

        Assert.Equal("Pending", status);
    }

    [Fact]
    public async Task The_application_can_write_the_five_lifecycle_columns()
    {
        var id = Guid.NewGuid();

        await InsertAsync(AppRole, await Shape(id: id, status: "Pending"));

        var affected = await ExecuteAsync(AppRole, CloseAsSent(id, "provider-ref"));

        Assert.Equal(1, affected);
    }

    [Theory]
    [InlineData("recipient", "'someone.else@example.com'")]
    [InlineData("notification_type", "'PasswordReset'")]
    [InlineData("token_id", "gen_random_uuid()")]
    [InlineData("created_at", "now()")]
    public async Task The_application_cannot_write_a_column_outside_the_lifecycle_set(
        string column, string value)
    {
        var id = Guid.NewGuid();

        await InsertAsync(AppRole, await Shape(id: id, status: "Pending"));

        var failure = await Refused(
            AppRole,
            $"""UPDATE "notification" SET "{column}" = {value} WHERE "id" = '{id}'""");

        // The grant refuses this, not the trigger. N8 is the second layer and
        // is proved separately as a superuser.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task The_application_cannot_delete_a_notification()
    {
        var id = Guid.NewGuid();

        await InsertAsync(AppRole, await Shape(
            id: id, status: "NotSent", reason: "Abandoned", closed: true));

        var failure = await Refused(
            AppRole, $"""DELETE FROM "notification" WHERE "id" = '{id}'""");

        // Not P0001: the row is terminal, so N10 would have permitted it. No
        // role holds DELETE in N1, and purge_role arrives with slice N3.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task Provisioning_holds_nothing_on_the_notification_table()
    {
        // A new table gets no privileges by accident: it has to say who may
        // read it. Provisioning writes the bootstrap token to a file and never
        // sends a notification.
        var failure = await Refused(
            _database.ConnectionFor(AuditBoundaryDatabase.ProvisioningRole),
            """SELECT count(*) FROM "notification" """);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // Fixture plumbing
    // ------------------------------------------------------------------

    private string Privileged => _database.PrivilegedConnection;

    private string AppRole
        => _database.ConnectionFor(AuditBoundaryDatabase.AppRole);

    /// <summary>
    /// Seeds a fresh token — and the identity and user it hangs from — then
    /// builds a notification INSERT against it. Every shape needs its own
    /// token because of N2.
    /// </summary>
    private async Task<string> Shape(
        Guid? id = null,
        string type = "AccountActivation",
        string status = "Pending",
        string? reason = null,
        bool attempted = false,
        bool closed = false,
        string? transportMessageId = null)
        => Insert(
            id ?? Guid.NewGuid(),
            await SeedTokenAsync(),
            type,
            status,
            reason,
            attempted,
            closed,
            transportMessageId);

    private static string Insert(
        Guid id,
        Guid tokenId,
        string type = "AccountActivation",
        string status = "Pending",
        string? reason = null,
        bool attempted = false,
        bool closed = false,
        string? transportMessageId = null)
        => $"""
            INSERT INTO "notification"
                ("id","notification_type","token_id","recipient","status",
                 "not_sent_reason","attempted_at","closed_at","transport_message_id")
            VALUES
                ('{id}','{type}','{tokenId}','john.smith@example.com','{status}',
                 {Literal(reason)},
                 {(attempted ? "now()" : "NULL")},
                 {(closed ? "now()" : "NULL")},
                 {Literal(transportMessageId)})
            """;

    private static string CloseAsSent(Guid id, string? transportMessageId = null)
        => $"""
            UPDATE "notification"
            SET "status" = 'Sent',
                "attempted_at" = now(),
                "closed_at" = now(),
                "transport_message_id" = {Literal(transportMessageId)}
            WHERE "id" = '{id}'
            """;

    private static string Literal(string? value)
        => value is null ? "NULL" : $"'{value}'";

    private async Task<Guid> SeedTokenAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");

        await ExecuteAsync(
            Privileged,
            $"""
             INSERT INTO "app_user"
                 ("id","actor_type","first_name","last_name","display_name","email",
                  "status","created_at","created_by","updated_at","updated_by")
             VALUES
                 ('{userId}','Human','John','Smith','John Smith','{unique}@example.com',
                  'Active', now(), '{userId}', now(), '{userId}');

             INSERT INTO "user_identity"
                 ("id","user_id","actor_type","identity_type","identity_provider",
                  "subject_id","username","status","created_at","created_by")
             VALUES
                 ('{identityId}','{userId}','Human','Local','Application',
                  '{identityId}','user{unique}','Active', now(), '{userId}');

             INSERT INTO "user_token"
                 ("id","user_identity_id","token_type","token_hash","expires_at",
                  "created_at","created_by")
             VALUES
                 ('{tokenId}','{identityId}','Activation','hash-{unique}',
                  now() + interval '72 hours', now(), '{userId}');
             """);

        return tokenId;
    }

    private static async Task InsertAsync(string connectionString, string sql)
    {
        var affected = await ExecuteAsync(connectionString, sql);

        Assert.Equal(1, affected);
    }

    private static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Asserts the statement was refused and hands back the error, so each test
    /// can be specific about WHY. A privilege refusal and a trigger refusal are
    /// different controls, and a test that accepted either would not notice one
    /// of them disappearing.
    /// </summary>
    private static async Task<PostgresException> Refused(
        string connectionString,
        string sql)
        => await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connectionString, sql));
}
