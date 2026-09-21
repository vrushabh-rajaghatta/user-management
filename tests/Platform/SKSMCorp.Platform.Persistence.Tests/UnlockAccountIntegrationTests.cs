using System.Text.Json;
using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Commands.SignIn;
using SKSMCorp.Platform.Application.Users.Commands.UnlockAccount;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Services;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// CRD-C6 end to end: an established administrator, dispatch through the
/// pipeline, and the rows read back.
///
/// EVERY REFUSAL is compared against a footprint that includes the absence of
/// any AccountUnlocked record, not only the credential's state — a refusal that
/// wrote an event and then rolled the credential back would otherwise pass.
///
/// Lock instants are computed from the application clock the handler reads, so
/// the live/expired boundary is exact rather than hostage to clock drift
/// between the application and the database.
///
/// Its own provisioned database: the unlocked account signs in afterwards and
/// becomes the actor of audit records.
/// </summary>
public sealed class UnlockAccountIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private const string Reason = "SUP-4471: lockout after mistyped password, user confirmed by phone.";
    private const string Password = "the-target-password-1";

    private readonly ActivationDatabase _database;

    public UnlockAccountIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // It works
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_live_lock_is_cleared_and_recorded_exactly()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(LockState.Live);
        var before = await ReadCredentialAsync(target.IdentityId);

        await DispatchAsync(admin.UserId, target.IdentityId);

        var after = await ReadCredentialAsync(target.IdentityId);

        Assert.Null(after.LockedUntil);
        Assert.Equal(0, after.FailedAttemptCount);

        // Only the lock. Nothing about the password moves.
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(before.ChangedAt, after.ChangedAt);
        Assert.Equal(before.MustChangePassword, after.MustChangePassword);

        var record = Assert.Single(await ReadRecordsAsync(target.IdentityId));

        Assert.Equal("Authenticated", record.OriginKind);
        Assert.Equal(admin.UserId.Value, record.ActorUserId);
        Assert.Equal(Reason, record.Reason);
        Assert.Equal("Credential", record.EntityType);
        Assert.Equal(1, await CountRefsAsync(target.UserId.Value, "User", "Subject"));

        Assert.Equal(target.LockedUntil, ReadInstant(record.Before, "LockedUntil"));
        Assert.Equal(target.FailedAttemptCount, ReadInt(record.Before, "FailedAttemptCount"));
        Assert.Null(ReadInstant(record.After, "LockedUntil"));
        Assert.Equal(0, ReadInt(record.After, "FailedAttemptCount"));
    }

    /// <summary>
    /// The proof that matters to the user: the real sign-in path, which
    /// refused them before, now lets them in.
    /// </summary>
    [Fact]
    public async Task The_unlocked_account_can_sign_in()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(LockState.Live);

        Assert.False((await SignInAsync(target.Username)).Succeeded);

        await DispatchAsync(admin.UserId, target.IdentityId);

        Assert.True((await SignInAsync(target.Username)).Succeeded);
    }

    // ------------------------------------------------------------------
    // Not currently locked (D1, D2)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(LockState.ExpiredAtThreshold))]
    [InlineData(nameof(LockState.CountedNoLock))]
    [InlineData(nameof(LockState.Clean))]
    public async Task A_credential_without_a_live_lock_is_refused(string state)
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(Enum.Parse<LockState>(state));

        await AssertRefusedAsync(target, () => DispatchAsync(admin.UserId, target.IdentityId));
    }

    // ------------------------------------------------------------------
    // Eligibility (D3) — each seeded with a live lock
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(Ineligible.ExternalIdentity))]
    [InlineData(nameof(Ineligible.AgentIdentity))]
    [InlineData(nameof(Ineligible.InactiveIdentity))]
    [InlineData(nameof(Ineligible.InactiveUser))]
    [InlineData(nameof(Ineligible.NoCredential))]
    public async Task An_ineligible_identity_is_refused(string reason)
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(LockState.Live, Enum.Parse<Ineligible>(reason));

        await AssertRefusedAsync(target, () => DispatchAsync(admin.UserId, target.IdentityId));
    }

    [Fact]
    public async Task An_unknown_identity_is_refused()
    {
        var admin = await CallerAsync("user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, Guid.NewGuid()));
    }

    // ------------------------------------------------------------------
    // Self-unlock (D4)
    // ------------------------------------------------------------------

    /// <summary>
    /// The same locked identity, two administrators: its owner is refused, a
    /// different administrator succeeds. The difference is who asks, so the
    /// refusal is about self-targeting and nothing else.
    /// </summary>
    [Fact]
    public async Task An_administrator_cannot_unlock_themselves_but_another_can()
    {
        var target = await SeedAsync(LockState.Live);
        await GrantAsync(target.UserId, "user-administrator");

        var other = await CallerAsync("user-administrator");

        await AssertRefusedAsync(target, () => DispatchAsync(target.UserId, target.IdentityId));

        await DispatchAsync(other.UserId, target.IdentityId);

        Assert.Null((await ReadCredentialAsync(target.IdentityId)).LockedUntil);
    }

    // ------------------------------------------------------------------
    // The reason, and callers who may not
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(LockState.Live);

        await AssertRefusedAsync(target, () => DispatchAsync(admin.UserId, target.IdentityId, reason));
    }

    /// <summary>access-reviewer holds user.read but not user.unlock.</summary>
    [Fact]
    public async Task A_caller_holding_a_different_user_permission_is_refused()
    {
        var reviewer = await CallerAsync("access-reviewer");
        var target = await SeedAsync(LockState.Live);

        await AssertRefusedAsync(target, () => DispatchAsync(reviewer.UserId, target.IdentityId));
    }

    [Fact]
    public async Task A_caller_holding_no_role_is_refused()
    {
        var caller = await CallerAsync(null);
        var target = await SeedAsync(LockState.Live);

        await AssertRefusedAsync(target, () => DispatchAsync(caller.UserId, target.IdentityId));
    }

    /// <summary>
    /// The caller holds user.unlock; only the actor type differs from success.
    /// </summary>
    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(LockState.Live);

        await AssertRefusedAsync(
            target, () => DispatchAsync(admin.UserId, target.IdentityId, actorType: ActorType.Agent));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var target = await SeedAsync(LockState.Live);
        var before = await FootprintAsync(target);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => DispatchAsync(caller: null, target.IdentityId));

        Assert.Equal(before, await FootprintAsync(target));
    }

    // ------------------------------------------------------------------

    private enum LockState
    {
        Live,
        ExpiredAtThreshold,
        CountedNoLock,
        Clean,
    }

    private enum Ineligible
    {
        None,
        ExternalIdentity,
        AgentIdentity,
        InactiveIdentity,
        InactiveUser,
        NoCredential,
    }

    private sealed record Target(
        UserId UserId, Guid IdentityId, string Username,
        DateTimeOffset? LockedUntil, int FailedAttemptCount);

    private sealed record CredentialRow(
        string Hash, string ChangedAt, bool MustChangePassword,
        int FailedAttemptCount, string? LockedUntil);

    private sealed record RecordRow(
        string OriginKind, Guid? ActorUserId, string? Reason, string EntityType,
        string? Before, string? After);

    /// <summary>Everything CRD-C6 could write, for one identity.</summary>
    private sealed record Footprint(
        int FailedAttemptCount, string? LockedUntil, long AccountUnlockedRecords);

    private async Task AssertRefusedAsync(Target target, Func<Task> dispatch)
    {
        var before = await FootprintAsync(target);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);

        var after = await FootprintAsync(target);

        Assert.Equal(before, after);
        Assert.Equal(0, after.AccountUnlockedRecords);
    }

    private async Task<Footprint> FootprintAsync(Target target)
    {
        var credential = await TryReadCredentialAsync(target.IdentityId);

        return new Footprint(
            credential?.FailedAttemptCount ?? -1,
            credential?.LockedUntil,
            (await ReadRecordsAsync(target.IdentityId)).Count);
    }

    private async Task<PermanentCaller> CallerAsync(string? roleCode)
        => await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, $"crd-c6-{roleCode ?? "unprivileged"}", roleCode);

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task DispatchAsync(
        UserId? caller, Guid identityId, string reason = Reason,
        ActorType actorType = ActorType.Human)
    {
        await using var provider = BuildProvider();

        using var scope = provider.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider
                .GetRequiredService<IExecutionContextInitializer>()
                .Establish(
                    caller,
                    actorType,
                    actorType == ActorType.Human
                        ? TestActorIdentity.Human()
                        : TestActorIdentity.NonHuman());
        }

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<UnlockAccountCommand, UnlockAccountResult>(
                new UnlockAccountCommand(new UserIdentityId(identityId), reason),
                CancellationToken.None);
    }

    private async Task<SignInResult> SignInAsync(string username)
    {
        await using var provider = BuildProvider();

        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignInCommand, SignInResult>(
                new SignInCommand(username, Password, null, "unlock-tests/1.0"),
                CancellationToken.None);
    }

    private async Task<Target> SeedAsync(LockState state, Ineligible ineligible = Ineligible.None)
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = SecurityBaseline.Current.MaxFailedLoginAttempts;

        // Whole seconds, so the instant survives the round trip through the
        // record's canonical JSON unchanged.
        var instant = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero);

        var (lockedUntil, count) = state switch
        {
            LockState.Live => ((DateTimeOffset?)instant.AddMinutes(30), threshold),
            LockState.ExpiredAtThreshold => ((DateTimeOffset?)instant.AddSeconds(-5), threshold),
            LockState.CountedNoLock => ((DateTimeOffset?)null, threshold - 1),
            LockState.Clean => ((DateTimeOffset?)null, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var username = $"unlock-{unique[..20]}";
        var system = User.SystemUserId.Value;

        var actorType = ineligible == Ineligible.AgentIdentity ? "Agent" : "Human";
        var identityType = ineligible == Ineligible.ExternalIdentity ? "External" : "Local";
        var provider = ineligible == Ineligible.ExternalIdentity ? "EntraId" : "Application";
        var subject = ineligible == Ineligible.ExternalIdentity ? $"external-{unique}" : identityId.ToString();
        var userActive = ineligible != Ineligible.InactiveUser;
        var identityActive = ineligible != Ineligible.InactiveIdentity;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  deactivated_at, deactivated_by,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', '{actorType}', 'Unlock', 'Target', 'Unlock Target',
                  'unlock-{unique}@example.test', '{(userActive ? "Active" : "Inactive")}',
                  {(userActive ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, deactivated_at, deactivated_by,
                  created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', '{actorType}', '{identityType}', '{provider}',
                  '{subject}', '{username}', '{(identityActive ? "Active" : "Inactive")}',
                  {(identityActive ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}');
             """);

        if (ineligible != Ineligible.NoCredential)
        {
            var hashed = new PasswordHasher().Hash(Password);

            await using var connection = await _database.OpenAsync();

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO credential
                    (id, user_identity_id, identity_type, password_hash,
                     password_algorithm, password_changed_at, must_change_password,
                     failed_attempt_count, locked_until, created_at, created_by)
                VALUES
                    (@id, @identity, @identityType, @hash, @algorithm,
                     now() - interval '1 day', false, @count, @lockedUntil,
                     now() - interval '1 day', @system)
                """, connection);

            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("identity", identityId);
            command.Parameters.AddWithValue("identityType", identityType);
            command.Parameters.AddWithValue("hash", hashed.Hash);
            command.Parameters.AddWithValue("algorithm", hashed.Algorithm);
            command.Parameters.AddWithValue("count", count);
            command.Parameters.AddWithValue("lockedUntil", lockedUntil is { } l ? l : DBNull.Value);
            command.Parameters.AddWithValue("system", system);

            await command.ExecuteNonQueryAsync();
        }

        return new Target(new UserId(userId), identityId, username, lockedUntil, count);
    }

    private async Task GrantAsync(UserId userId, string roleCode)
        => await ExecuteAsync(
            $"""
             INSERT INTO user_role
                 (id, user_id, actor_type, role_id, scope_type, scope_id,
                  effective_from, effective_to, assigned_at, assigned_by,
                  assignment_reason)
             SELECT '{Guid.NewGuid()}', '{userId.Value}', 'Human', r.id, 'Global', NULL,
                    now() - interval '1 day', NULL, now(), '{User.SystemUserId.Value}',
                    'CRD-C6 self-unlock test.'
             FROM role r WHERE r.code = '{roleCode}'
             """);

    private async Task<CredentialRow> ReadCredentialAsync(Guid identityId)
        => await TryReadCredentialAsync(identityId)
           ?? throw new InvalidOperationException("The credential row is missing.");

    private async Task<CredentialRow?> TryReadCredentialAsync(Guid identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_changed_at::text, must_change_password,
                   failed_attempt_count, locked_until::text
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
            reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private async Task<IReadOnlyList<RecordRow>> ReadRecordsAsync(Guid identityId)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.origin_kind, r.actor_user_id, r.reason, r.entity_type,
                   r.before::text, r.after::text
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'AccountUnlocked'
              AND e.entity_type = 'Identity' AND e.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private async Task<long> CountRefsAsync(Guid entityId, string entityType, string role)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'AccountUnlocked'
              AND e.entity_type = @type AND e.entity_id = @id AND e.ref_role = @role
            """, connection);

        command.Parameters.AddWithValue("type", entityType);
        command.Parameters.AddWithValue("id", entityId);
        command.Parameters.AddWithValue("role", role);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static DateTimeOffset? ReadInstant(string? json, string property)
    {
        using var document = JsonDocument.Parse(json ?? "{}");

        var value = document.RootElement.GetProperty(property);

        return value.ValueKind == JsonValueKind.Null
            ? null
            : DateTimeOffset.Parse(value.GetString()!).ToUniversalTime();
    }

    private static int ReadInt(string? json, string property)
    {
        using var document = JsonDocument.Parse(json ?? "{}");

        return document.RootElement.GetProperty(property).GetInt32();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
