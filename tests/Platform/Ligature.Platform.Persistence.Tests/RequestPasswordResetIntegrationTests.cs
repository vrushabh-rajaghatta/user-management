using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.RequestPasswordReset;
using Ligature.Platform.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C2 end to end: dispatch through the real pipeline, real adapters, rows
/// read back from PostgreSQL.
///
/// TWO BRANCHES, AND THE SILENT ONE IS THE HARDER CLAIM. That a matching
/// request issues a token is easy to assert. That a non-matching one writes
/// NOTHING — no token, no notification, no audit record — has to be checked
/// against all three tables every time, because the enumeration risk is not
/// that the response differs but that something observable does.
///
/// The byte-identical response is proved at the HTTP boundary instead, in
/// PasswordResetRequestEndpointTests, because that is where a response
/// actually exists.
///
/// No execution context is established. CRD-C2 is anonymous — somebody who
/// cannot sign in is asking for a way back — and the audit records it writes
/// are attributed to the System actor by declaration (AsSystem), never by the
/// caller. A test that established one would be testing a path the endpoint
/// cannot produce.
///
/// Its own provisioned database, for ActivationDatabase's reason: the accounts
/// these tests create become audit subjects, and no role may delete an audit
/// row.
/// </summary>
public sealed class RequestPasswordResetIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public RequestPasswordResetIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // The issuing branch
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_known_local_account_is_issued_a_reset_token()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Email);

        var tokens = await ReadTokensAsync(subject.IdentityId);

        var token = Assert.Single(tokens);
        Assert.Equal("PasswordReset", token.TokenType);
        Assert.Null(token.UsedAt);
        Assert.Null(token.InvalidatedAt);

        // SYSTEM_UUID, not the subject: the person at the keyboard is not
        // authenticated and may not be the account owner, so recording the
        // owner as issuer would assert an act they may not have performed.
        Assert.Equal(User.SystemUserId.Value, token.CreatedBy);
    }

    [Fact]
    public async Task The_account_can_also_be_found_by_username()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Username);

        Assert.Single(await ReadTokensAsync(subject.IdentityId));
    }

    [Fact]
    public async Task A_pending_notification_is_written_for_the_subjects_address()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Email);

        var row = Assert.Single(await ReadNotificationsAsync(subject.IdentityId));

        Assert.Equal("PasswordReset", row.Type);
        Assert.Equal("Pending", row.Status);
        Assert.Equal(subject.Email, row.Recipient);
    }

    [Fact]
    public async Task The_request_is_recorded_as_the_System_actor()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Email);

        var record = Assert.Single(
            await ReadRecordsAsync(subject.IdentityId, "PasswordResetRequested"));

        // The catalogue permits ONLY a System origin for this event, so an
        // anonymous scope would have produced Anonymous and been refused by
        // behaviour 14. AsSystem is required here, not chosen.
        Assert.Equal("System", record.OriginKind);
        Assert.Equal(User.SystemUserId.Value, record.ActorUserId);
        Assert.Equal("System", record.ActorType);

        // Never the token or its hash.
        Assert.DoesNotContain("hash", record.Payload ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // UT5 — supersession
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_second_request_supersedes_the_first_token()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Email);
        var first = Assert.Single(await ReadTokensAsync(subject.IdentityId));

        await DispatchAsync(subject.Email);

        var tokens = await ReadTokensAsync(subject.IdentityId);

        Assert.Equal(2, tokens.Count);

        // Only one live. If an earlier mail were intercepted, the token it
        // carried has already stopped working.
        Assert.Single(tokens.Where(x => x.InvalidatedAt is null && x.UsedAt is null));

        var superseded = tokens.Single(x => x.Id == first.Id);
        Assert.NotNull(superseded.InvalidatedAt);
    }

    [Fact]
    public async Task Each_superseded_token_gets_its_own_TokenInvalidated_record()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Email);
        var first = Assert.Single(await ReadTokensAsync(subject.IdentityId));

        await DispatchAsync(subject.Email);

        // One per token, not one per batch: a reviewer asking "what happened
        // to the link I was sent" needs the record for THAT token.
        var invalidations =
            await ReadRecordsAsync(subject.IdentityId, "TokenInvalidated");

        var record = Assert.Single(invalidations);
        Assert.Equal(first.Id, record.EntityId);
        Assert.Equal("System", record.OriginKind);
    }

    /// <summary>
    /// The clause most likely to be dropped as pointless, and the one UT4
    /// depends on.
    ///
    /// UT4's index is "unused AND uninvalidated" and cannot mention expiry —
    /// a partial index predicate must be immutable and now() is not. So an
    /// expired-but-unused token still holds the slot, and a UT5 that skipped
    /// expired tokens would make the next issuance collide rather than
    /// succeed.
    /// </summary>
    [Fact]
    public async Task An_expired_unused_token_is_superseded_too()
    {
        var subject = await SeedAsync();

        var expired = await SeedTokenAsync(
            subject.IdentityId, TokenType.PasswordReset, expired: true);

        await DispatchAsync(subject.Email);

        var tokens = await ReadTokensAsync(subject.IdentityId);

        Assert.NotNull(tokens.Single(x => x.Id == expired).InvalidatedAt);
        Assert.Single(tokens.Where(x => x.InvalidatedAt is null && x.UsedAt is null));
    }

    [Fact]
    public async Task An_activation_token_is_not_superseded_by_a_reset_request()
    {
        var subject = await SeedAsync();

        var activation = await SeedTokenAsync(
            subject.IdentityId, TokenType.Activation, expired: false);

        await DispatchAsync(subject.Email);

        var tokens = await ReadTokensAsync(subject.IdentityId);

        // UT5 is scoped to the type being issued. Superseding the activation
        // token would strand a user who had not activated yet.
        Assert.Null(tokens.Single(x => x.Id == activation).InvalidatedAt);
    }

    // ------------------------------------------------------------------
    // The silent branch — nothing, anywhere
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive-user")]
    [InlineData("inactive-identity")]
    [InlineData("external-identity")]
    [InlineData("no-email")]
    [InlineData("blank")]
    public async Task An_ineligible_request_writes_nothing(string scenario)
    {
        var subject = await SeedAsync(
            userStatus: scenario == "inactive-user" ? "Inactive" : "Active",
            identityStatus: scenario == "inactive-identity" ? "Inactive" : "Active",
            identityType: scenario == "external-identity" ? "External" : "Local",
            withEmail: scenario != "no-email");

        var input = scenario switch
        {
            "unknown" => $"nobody-{Guid.NewGuid():N}@example.test",
            "blank" => "   ",
            "no-email" => subject.Username,
            _ => subject.Email ?? subject.Username,
        };

        var before = await CountRecordsAsync();

        await DispatchAsync(input);

        Assert.Empty(await ReadTokensAsync(subject.IdentityId));
        Assert.Empty(await ReadNotificationsAsync(subject.IdentityId));

        // The trail too. A PasswordResetRequested for a request that issued
        // nothing would be the oracle in another table.
        Assert.Equal(before, await CountRecordsAsync());
    }

    /// <summary>
    /// D2's collision, and why "exactly one" rather than a precedence rule.
    ///
    /// Usernames are unconstrained labels, so one person's username can equal
    /// another person's email. Username-first would silently favour the label
    /// holder; email-first would favour the mailbox holder. Neither is
    /// defensible, so both are refused and an administrator can still use
    /// CRD-C5.
    /// </summary>
    [Fact]
    public async Task A_username_colliding_with_another_accounts_email_resolves_to_neither()
    {
        var shared = $"collide-{Guid.NewGuid():N}@example.test";

        var byEmail = await SeedAsync(email: shared);
        var byUsername = await SeedAsync(username: shared);

        await DispatchAsync(shared);

        Assert.Empty(await ReadTokensAsync(byEmail.IdentityId));
        Assert.Empty(await ReadTokensAsync(byUsername.IdentityId));
    }

    // ------------------------------------------------------------------

    private async Task DispatchAsync(string? emailOrUsername)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        // No IExecutionContextInitializer.Establish: CRD-C2 is anonymous.
        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<RequestPasswordResetCommand, RequestPasswordResetResult>(
                new RequestPasswordResetCommand(
                    emailOrUsername ?? string.Empty, "203.0.113.7"),
                CancellationToken.None);
    }

    private sealed record Subject(
        Guid UserId, Guid IdentityId, string Username, string? Email);

    private sealed record TokenRow(
        Guid Id, string TokenType, DateTimeOffset? UsedAt,
        DateTimeOffset? InvalidatedAt, Guid CreatedBy);

    private sealed record NotificationRow(string Type, string Status, string Recipient);

    private sealed record RecordRow(
        string OriginKind, Guid? ActorUserId, string? ActorType,
        Guid? EntityId, string? Payload);

    private async Task<Subject> SeedAsync(
        string userStatus = "Active",
        string identityStatus = "Active",
        string identityType = "Local",
        bool withEmail = true,
        string? email = null,
        string? username = null)
    {
        var unique = Guid.NewGuid().ToString("N");

        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();

        var resolvedEmail = withEmail ? email ?? $"reset-{unique}@example.test" : null;
        var resolvedUsername = username ?? $"reset-{unique}";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Reset', 'Subject', 'Reset Subject',
                  {(resolvedEmail is null ? "NULL" : $"'{resolvedEmail}'")},
                  '{userStatus}', now(), '{User.SystemUserId.Value}',
                  now(), '{User.SystemUserId.Value}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', '{identityType}',
                  '{(identityType == "Local" ? "Application" : "Okta")}',
                  '{identityId}', '{resolvedUsername}', '{identityStatus}',
                  now(), '{User.SystemUserId.Value}');
             """);

        return new Subject(userId, identityId, resolvedUsername, resolvedEmail);
    }

    private async Task<Guid> SeedTokenAsync(
        Guid identityId, TokenType type, bool expired)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{id}', '{identityId}', '{type}', 'hash-{Guid.NewGuid():N}',
                  {(expired ? "now() - interval '1 hour'" : "now() + interval '1 hour'")},
                  NULL, NULL,
                  {(expired ? "now() - interval '2 hours'" : "now()")},
                  '{User.SystemUserId.Value}')
             """);

        return id;
    }

    private async Task<IReadOnlyList<TokenRow>> ReadTokensAsync(Guid identityId)
    {
        var rows = new List<TokenRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT id, token_type, used_at, invalidated_at, created_by
            FROM user_token WHERE user_identity_id = @id ORDER BY created_at
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new TokenRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetGuid(4)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<NotificationRow>> ReadNotificationsAsync(
        Guid identityId)
    {
        var rows = new List<NotificationRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT n.notification_type, n.status, n.recipient
            FROM notification n
            JOIN user_token t ON t.id = n.token_id
            WHERE t.user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            rows.Add(new NotificationRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        return rows;
    }

    private async Task<IReadOnlyList<RecordRow>> ReadRecordsAsync(
        Guid identityId, string eventType)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.origin_kind, r.actor_user_id, r.actor_type,
                   r.entity_id, r.payload::text
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type
              AND e.entity_type = 'Identity'
              AND e.entity_id = @id
            ORDER BY r.sequence
            """, connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private async Task<long> CountRecordsAsync()
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_record", connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
