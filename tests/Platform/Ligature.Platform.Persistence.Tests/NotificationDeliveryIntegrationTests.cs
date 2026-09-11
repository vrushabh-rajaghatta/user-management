using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Notifications;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The three database collaborators against a real database, run AS app_role.
///
/// The role matters. These are the statements the sender actually issues at
/// runtime, so running them under the same grant the host holds proves the
/// Phase A privilege model is sufficient for the real work — not merely that
/// the SQL parses. A column-level UPDATE grant that was one column short would
/// fail here and nowhere else.
/// </summary>
public sealed class NotificationDeliveryIntegrationTests : IAsyncLifetime
{
    private AuditBoundaryDatabase _database = null!;

    public async Task InitializeAsync()
        => _database = await AuditBoundaryDatabase.CreateAsync();

    public async Task DisposeAsync()
        => await _database.DisposeAsync();

    // ------------------------------------------------------------------
    // The gate
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_live_token_for_an_active_subject_is_eligible()
        => Assert.Equal(
            NotificationEligibility.Eligible,
            await EvaluateAsync(await SeedAsync()));

    [Fact]
    public async Task A_consumed_token_is_not_live()
    {
        var seeded = await SeedAsync();

        await ExecuteAsync(
            $"UPDATE user_token SET used_at = now() WHERE id = '{seeded.TokenId}'");

        Assert.Equal(
            NotificationEligibility.TokenNotLive, await EvaluateAsync(seeded));
    }

    [Fact]
    public async Task A_superseded_token_is_not_live()
    {
        var seeded = await SeedAsync();

        await ExecuteAsync(
            $"UPDATE user_token SET invalidated_at = now() WHERE id = '{seeded.TokenId}'");

        // The case UT5 exists for: a newer issuance invalidated this one
        // between the row being written and the send being attempted.
        Assert.Equal(
            NotificationEligibility.TokenNotLive, await EvaluateAsync(seeded));
    }

    [Fact]
    public async Task An_expired_token_is_not_live()
    {
        var seeded = await SeedAsync();

        // Both, because user_token carries its own CHECK that expiry follows
        // creation — a token cannot be made to have expired without also
        // having been issued earlier.
        await ExecuteAsync(
            $"""
             UPDATE user_token
             SET created_at = now() - interval '2 hours',
                 expires_at = now() - interval '1 hour'
             WHERE id = '{seeded.TokenId}'
             """);

        Assert.Equal(
            NotificationEligibility.TokenNotLive, await EvaluateAsync(seeded));
    }

    [Fact]
    public async Task A_deactivated_identity_makes_the_subject_inactive()
    {
        var seeded = await SeedAsync();

        await ExecuteAsync(
            $"UPDATE user_identity SET status = 'Inactive' WHERE id = '{seeded.IdentityId}'");

        // Notification's own condition. Sending would be harmless — sign-in
        // fails on identity status — but a mail inviting a deactivated person
        // to activate an account is a support ticket.
        Assert.Equal(
            NotificationEligibility.SubjectInactive, await EvaluateAsync(seeded));
    }

    [Fact]
    public async Task A_deactivated_user_makes_the_subject_inactive()
    {
        var seeded = await SeedAsync();

        await ExecuteAsync(
            $"UPDATE app_user SET status = 'Inactive' WHERE id = '{seeded.UserId}'");

        Assert.Equal(
            NotificationEligibility.SubjectInactive, await EvaluateAsync(seeded));
    }

    [Fact]
    public async Task When_both_conditions_fail_the_token_is_reported()
    {
        var seeded = await SeedAsync();

        await ExecuteAsync(
            $"UPDATE user_token SET used_at = now() WHERE id = '{seeded.TokenId}'");
        await ExecuteAsync(
            $"UPDATE app_user SET status = 'Inactive' WHERE id = '{seeded.UserId}'");

        // Token first: it is the condition that makes the message useless
        // rather than merely inappropriate.
        Assert.Equal(
            NotificationEligibility.TokenNotLive, await EvaluateAsync(seeded));
    }

    // ------------------------------------------------------------------
    // The terminal writer
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_sent_outcome_is_recorded_with_its_reference()
    {
        var seeded = await SeedAsync();
        var attempted = DateTimeOffset.UtcNow;

        await Writer().CloseAsync(
            seeded.NotificationId,
            new NotificationOutcome(
                NotificationStatus.Sent, null, attempted, "provider-ref"),
            CancellationToken.None);

        var row = await ReadAsync(seeded.NotificationId);

        Assert.Equal("Sent", row.Status);
        Assert.Null(row.Reason);
        Assert.NotNull(row.AttemptedAt);
        Assert.NotNull(row.ClosedAt);
        Assert.Equal("provider-ref", row.MessageId);
    }

    [Fact]
    public async Task A_transport_failure_is_recorded_without_a_reference()
    {
        var seeded = await SeedAsync();

        await Writer().CloseAsync(
            seeded.NotificationId,
            new NotificationOutcome(
                NotificationStatus.NotSent,
                NotSentReason.TransportFailed,
                DateTimeOffset.UtcNow,
                null),
            CancellationToken.None);

        var row = await ReadAsync(seeded.NotificationId);

        Assert.Equal("NotSent", row.Status);
        Assert.Equal("TransportFailed", row.Reason);
        Assert.Null(row.MessageId);
    }

    [Fact]
    public async Task Closing_a_row_that_is_already_terminal_is_refused_by_the_trigger()
    {
        var seeded = await SeedAsync();
        var writer = Writer();

        await writer.CloseAsync(
            seeded.NotificationId,
            new NotificationOutcome(
                NotificationStatus.Sent, null, DateTimeOffset.UtcNow, "first"),
            CancellationToken.None);

        // A SECOND independent close is a defect, not a retry, and N9 refuses
        // it. The same refusal is what makes the writer's own single retry safe
        // — there it proves the first write committed and is treated as
        // success; here nothing claimed that, so it propagates.
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => writer.CloseAsync(
                seeded.NotificationId,
                new NotificationOutcome(
                    NotificationStatus.NotSent,
                    NotSentReason.TransportFailed,
                    DateTimeOffset.UtcNow,
                    null),
                CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Equal("first", (await ReadAsync(seeded.NotificationId)).MessageId);
    }

    // ------------------------------------------------------------------
    // The sweeper
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_row_older_than_the_grace_window_becomes_abandoned()
    {
        var seeded = await SeedAsync(ageMinutes: 30);

        var swept = await Sweeper().SweepAsync(
            TimeSpan.FromMinutes(5), 100, CancellationToken.None);

        Assert.Equal(1, swept);

        var row = await ReadAsync(seeded.NotificationId);

        Assert.Equal("NotSent", row.Status);
        Assert.Equal("Abandoned", row.Reason);
        Assert.NotNull(row.ClosedAt);

        // The whole meaning of Abandoned: no attempt was observed, so none is
        // claimed. This is what distinguishes it from TransportFailed.
        Assert.Null(row.AttemptedAt);
    }

    [Fact]
    public async Task A_row_inside_the_grace_window_is_left_alone()
    {
        var seeded = await SeedAsync();

        await Sweeper().SweepAsync(
            TimeSpan.FromMinutes(5), 100, CancellationToken.None);

        // The sender may legitimately still be working on it.
        Assert.Equal("Pending", (await ReadAsync(seeded.NotificationId)).Status);
    }

    [Fact]
    public async Task Sweeping_twice_closes_nothing_the_second_time()
    {
        await SeedAsync(ageMinutes: 30);

        var sweeper = Sweeper();

        Assert.Equal(1, await sweeper.SweepAsync(
            TimeSpan.FromMinutes(5), 100, CancellationToken.None));

        // Idempotent by construction: a swept row no longer matches.
        Assert.Equal(0, await sweeper.SweepAsync(
            TimeSpan.FromMinutes(5), 100, CancellationToken.None));
    }

    [Fact]
    public async Task The_batch_size_bounds_one_sweep()
    {
        for (var i = 0; i < 3; i++)
            await SeedAsync(ageMinutes: 30);

        var sweeper = Sweeper();

        Assert.Equal(2, await sweeper.SweepAsync(
            TimeSpan.FromMinutes(5), 2, CancellationToken.None));

        Assert.Equal(1, await sweeper.SweepAsync(
            TimeSpan.FromMinutes(5), 2, CancellationToken.None));
    }

    [Fact]
    public async Task A_terminal_row_is_never_swept_again()
    {
        var seeded = await SeedAsync(ageMinutes: 30);

        await Writer().CloseAsync(
            seeded.NotificationId,
            new NotificationOutcome(
                NotificationStatus.Sent, null, DateTimeOffset.UtcNow, "ref"),
            CancellationToken.None);

        Assert.Equal(0, await Sweeper().SweepAsync(
            TimeSpan.FromMinutes(5), 100, CancellationToken.None));

        Assert.Equal("Sent", (await ReadAsync(seeded.NotificationId)).Status);
    }

    // ------------------------------------------------------------------
    // Fixture
    // ------------------------------------------------------------------

    private string AppRole
        => _database.ConnectionFor(AuditBoundaryDatabase.AppRole);

    private INotificationGate Gate() => new NotificationGate(AppRole);

    private INotificationTerminalWriter Writer()
        => new NotificationTerminalWriter(AppRole, TimeProvider.System);

    private INotificationSweeper Sweeper() => new NotificationSweeper(AppRole);

    private async Task<NotificationEligibility> EvaluateAsync(Seeded seeded)
        => await Gate().EvaluateAsync(
            new UserTokenId(seeded.TokenId), CancellationToken.None);

    private readonly record struct Seeded(
        Guid UserId, Guid IdentityId, Guid TokenId, NotificationId NotificationId);

    private readonly record struct Row(
        string Status,
        string? Reason,
        DateTimeOffset? AttemptedAt,
        DateTimeOffset? ClosedAt,
        string? MessageId);

    private async Task<Seeded> SeedAsync(int ageMinutes = 0)
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var notificationId = NotificationId.New();
        var unique = Guid.NewGuid().ToString("N");

        await ExecuteAsync(
            $"""
             INSERT INTO "app_user"
                 ("id","actor_type","first_name","last_name","display_name","email",
                  "status","created_at","created_by","updated_at","updated_by")
             VALUES ('{userId}','Human','John','Smith','John Smith','{unique}@example.com',
                  'Active', now(), '{userId}', now(), '{userId}');

             INSERT INTO "user_identity"
                 ("id","user_id","actor_type","identity_type","identity_provider",
                  "subject_id","username","status","created_at","created_by")
             VALUES ('{identityId}','{userId}','Human','Local','Application',
                  '{identityId}','user{unique}','Active', now(), '{userId}');

             INSERT INTO "user_token"
                 ("id","user_identity_id","token_type","token_hash","expires_at",
                  "created_at","created_by")
             VALUES ('{tokenId}','{identityId}','Activation','hash-{unique}',
                  now() + interval '72 hours', now(), '{userId}');

             INSERT INTO "notification"
                 ("id","notification_type","token_id","recipient","status","created_at")
             VALUES ('{notificationId.Value}','AccountActivation','{tokenId}',
                  '{unique}@example.com','Pending',
                  now() - interval '{ageMinutes} minutes');
             """);

        return new Seeded(userId, identityId, tokenId, notificationId);
    }

    private async Task<Row> ReadAsync(NotificationId id)
    {
        await using var connection = new NpgsqlConnection(_database.PrivilegedConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT status, not_sent_reason, attempted_at, closed_at, transport_message_id
            FROM notification WHERE id = @id
            """,
            connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new Row(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.PrivilegedConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
