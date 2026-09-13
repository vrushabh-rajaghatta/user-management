using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C5 end to end: an established administrator, dispatch through the
/// pipeline, and the rows read back from PostgreSQL.
///
/// THE INVARIANT EVERY REFUSAL IS HELD TO: an ineligible target leaves nothing
/// behind. Each refusal is seeded with a live prior reset token and compared
/// against a snapshot of every table the command writes — tokens, their
/// invalidation, the credential flag, notifications and audit references.
///
/// What that proves, precisely: nothing COMMITS. It does not prove the checks
/// run before the writes — the refusal rolls the transaction back, so a handler
/// that invalidated first and refused afterwards would leave the same
/// footprint. The ordering is a property of the handler's code, not something
/// these tests can observe.
///
/// Wording is not asserted: the refusal messages are not a frozen contract.
///
/// Its own provisioned database, for ActivationDatabase's reason: the
/// end-to-end case makes a subject the actor of audit records.
/// </summary>
public sealed class AdminResetPasswordIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private const string Reason = "The user reported a suspected compromise.";
    private const string Current = "the-current-password-1";
    private const string Fresh = "an-entirely-new-password-2";

    private readonly ActivationDatabase _database;

    public AdminResetPasswordIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // It works
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_administrator_issues_a_reset_token_attributed_to_themselves()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();
        var before = await ReadCredentialAsync(subject.IdentityId);

        await DispatchAsync(admin.UserId, subject.UserId);

        var token = Assert.Single(await ReadTokensAsync(subject.IdentityId));

        Assert.Equal("PasswordReset", token.TokenType);
        Assert.Null(token.UsedAt);
        Assert.Null(token.InvalidatedAt);

        // The administrator, never SYSTEM_UUID: this is what distinguishes an
        // administrator's reset from a self-service one in forensics (UM §6.5).
        Assert.Equal(admin.UserId.Value, token.CreatedBy);

        // Lifetime from the effective policy, a CAP.
        Assert.Equal(
            SecurityBaseline.Current.PasswordResetTokenLifetime.TotalSeconds,
            token.LifetimeSeconds,
            precision: 3);

        var after = await ReadCredentialAsync(subject.IdentityId);

        Assert.False(before.MustChangePassword);
        Assert.True(after.MustChangePassword);

        // Only the flag. The password did not change.
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(before.ChangedAt, after.ChangedAt);

        var notification = Assert.Single(await ReadNotificationsAsync(token.Id));

        Assert.Equal("AdminPasswordReset", notification.Type);
        Assert.Equal(subject.Email, notification.Recipient);
    }

    /// <summary>
    /// Issuing an administrative reset is not completing one. A locked account
    /// stays locked with its counters intact until CRD-C3 changes the password
    /// or CRD-C6 unlocks it.
    /// </summary>
    [Fact]
    public async Task A_locked_account_stays_locked_at_issuance()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync(lockedOut: true);
        var before = await ReadCredentialAsync(subject.IdentityId);

        await DispatchAsync(admin.UserId, subject.UserId);

        var after = await ReadCredentialAsync(subject.IdentityId);

        Assert.True(before.IsLocked);
        Assert.True(after.IsLocked);
        Assert.Equal(before.LockedUntil, after.LockedUntil);
        Assert.Equal(before.FailedAttemptCount, after.FailedAttemptCount);
        Assert.True(after.MustChangePassword);
    }

    /// <summary>
    /// UT5 — an EXPIRED but unused reset token is superseded too: it still
    /// holds UT4's slot, which cannot mention expiry. A used token is history
    /// and is not touched; an open activation token is another type and is not
    /// touched either.
    ///
    /// UT4 permits one open reset token per identity, so the expired token and
    /// a live self-service link cannot be seeded together. The live link has
    /// its own test below.
    /// </summary>
    [Fact]
    public async Task An_expired_unused_reset_token_is_superseded_and_nothing_else()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var used = await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset, used: true);
        var expired = await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset, expired: true);
        var activation = await InsertTokenAsync(subject.IdentityId, TokenType.Activation);

        await DispatchAsync(admin.UserId, subject.UserId);

        var tokens = (await ReadTokensAsync(subject.IdentityId)).ToDictionary(x => x.Id);

        Assert.NotNull(tokens[expired.Id].InvalidatedAt);
        Assert.Null(tokens[used.Id].InvalidatedAt);
        Assert.Null(tokens[activation.Id].InvalidatedAt);

        var issued = Assert.Single(
            tokens.Values,
            x => x.TokenType == "PasswordReset" && x.UsedAt is null && x.InvalidatedAt is null);

        Assert.Single(await ReadRecordsAsync(subject.IdentityId, "TokenInvalidated"));

        Assert.Equal(
            1,
            await CountRefsAsync("TokenInvalidated", "Token", issued.Id, "SupersededBy"));
    }

    /// <summary>
    /// A pending self-service link is superseded: one live reset link per
    /// identity, whoever asked for it.
    /// </summary>
    [Fact]
    public async Task A_live_self_service_reset_token_is_superseded()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var selfService = await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset);

        await DispatchAsync(admin.UserId, subject.UserId);

        var tokens = (await ReadTokensAsync(subject.IdentityId)).ToDictionary(x => x.Id);

        Assert.NotNull(tokens[selfService.Id].InvalidatedAt);

        var issued = Assert.Single(tokens.Values, x => x.InvalidatedAt is null);

        Assert.Equal(
            1,
            await CountRefsAsync("TokenInvalidated", "Token", issued.Id, "SupersededBy"));
    }

    [Fact]
    public async Task The_events_name_the_administrator_and_carry_the_reason()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset);

        var captured = new List<string>();

        await DispatchAsync(admin.UserId, subject.UserId, captured: captured);

        var token = Assert.Single(
            await ReadTokensAsync(subject.IdentityId),
            x => x.InvalidatedAt is null);

        foreach (var eventType in new[] { "AdminPasswordResetIssued", "TokenIssued", "TokenInvalidated" })
        {
            var record = Assert.Single(await ReadRecordsAsync(subject.IdentityId, eventType));

            // The administrator authorised this; nothing is AsSystem.
            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(admin.UserId.Value, record.ActorUserId);

            // NEVER the token, its secret or its hash.
            Assert.DoesNotContain(token.Hash, record.Payload ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(Assert.Single(captured), record.Payload ?? "", StringComparison.Ordinal);
        }

        var issuance = Assert.Single(
            await ReadRecordsAsync(subject.IdentityId, "AdminPasswordResetIssued"));

        Assert.Equal(Reason, issuance.Reason);
        Assert.Equal("true", issuance.MustChangePassword);

        Assert.Equal(1, await CountRefsAsync("AdminPasswordResetIssued", "Token", token.Id, "Issued"));
        Assert.Equal(1, await CountRefsAsync("AdminPasswordResetIssued", "User", subject.UserId.Value, "Subject"));
    }

    /// <summary>
    /// The link works, and it is the ONLY link that works. The self-service
    /// link issued before it has been superseded; the administrator's link
    /// completes through CRD-C3, which consumes it and clears the flag.
    /// </summary>
    [Fact]
    public async Task The_issued_link_completes_through_ResetPassword_and_the_old_link_does_not()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var selfService = await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset);

        var captured = new List<string>();

        await DispatchAsync(admin.UserId, subject.UserId, captured: captured);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => ResetAsync(selfService.PlainText, Fresh));

        await ResetAsync(Assert.Single(captured), Fresh);

        var credential = await ReadCredentialAsync(subject.IdentityId);

        Assert.False(credential.MustChangePassword);
        Assert.True(new PasswordHasher().Verify(Fresh, credential.Hash, "pbkdf2-sha256-v1").IsValid);

        Assert.Single(
            await ReadTokensAsync(subject.IdentityId),
            x => x.UsedAt is not null);
    }

    /// <summary>
    /// No stated prohibition, and the permission already gates it. The token
    /// still goes to the administrator's own mailbox; nothing about the
    /// password becomes known to anyone.
    /// </summary>
    [Fact]
    public async Task An_administrator_may_reset_their_own_password()
    {
        var subject = await SeedAsync();

        await GrantAsync(subject.UserId, "user-administrator");

        await DispatchAsync(subject.UserId, subject.UserId);

        var token = Assert.Single(await ReadTokensAsync(subject.IdentityId));

        Assert.Equal(subject.UserId.Value, token.CreatedBy);
        Assert.True((await ReadCredentialAsync(subject.IdentityId)).MustChangePassword);
    }

    /// <summary>
    /// "Exactly one LOCAL identity", not "exactly one identity". A user who
    /// also signs in through an external provider still has one local
    /// identity and one credential, and is eligible.
    /// </summary>
    [Fact]
    public async Task An_external_identity_beside_the_local_one_does_not_block_the_reset()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var external = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{external}', '{subject.UserId.Value}', 'Human', 'External', 'EntraId',
                  'external-{external:N}', NULL, 'Active',
                  now() - interval '1 day', '{User.SystemUserId.Value}')
             """);

        await DispatchAsync(admin.UserId, subject.UserId);

        Assert.Single(await ReadTokensAsync(subject.IdentityId));
        Assert.Empty(await ReadTokensAsync(external));
    }

    // ------------------------------------------------------------------
    // Refused, and nothing written
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId, reason));
    }

    [Theory]
    [InlineData(nameof(Scenario.InactiveUser))]
    [InlineData(nameof(Scenario.InactiveIdentity))]
    [InlineData(nameof(Scenario.NoLocalIdentity))]
    [InlineData(nameof(Scenario.TwoLocalIdentities))]
    [InlineData(nameof(Scenario.NoEmail))]
    [InlineData(nameof(Scenario.NoCredential))]
    [InlineData(nameof(Scenario.AgentUser))]
    public async Task An_ineligible_target_is_refused(string scenario)
    {
        var admin = await CallerAsync("user-administrator");

        var subject = Enum.Parse<Scenario>(scenario) switch
        {
            Scenario.InactiveUser => await SeedAsync(userActive: false),
            Scenario.InactiveIdentity => await SeedAsync(identityActive: false),
            Scenario.NoLocalIdentity => await SeedAsync(localIdentities: 0),
            Scenario.TwoLocalIdentities => await SeedAsync(localIdentities: 2),
            Scenario.NoEmail => await SeedAsync(withEmail: false),
            Scenario.NoCredential => await SeedAsync(withCredential: false),

            // AU11 blocks agent creation in the application only; the schema
            // accepts an agent with a local identity, an email and a
            // credential. The actor-type check is the only thing that refuses
            // this target — every other predicate passes.
            Scenario.AgentUser => await SeedAsync(actorType: "Agent"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId));
    }

    [Fact]
    public async Task An_unknown_user_is_refused()
    {
        var admin = await CallerAsync("user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, UserId.New()));
    }

    /// <summary>
    /// The System actor cannot authenticate (UI8), so it has no password to
    /// reset — and it is the one non-human account that always exists.
    /// </summary>
    [Fact]
    public async Task The_system_actor_is_refused()
    {
        var admin = await CallerAsync("user-administrator");

        var tokensBefore = await ScalarAsync<long>("SELECT count(*) FROM user_token");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, User.SystemUserId));

        // Bounded by this fixture's own database, so concurrent classes cannot
        // disturb the count.
        Assert.Equal(tokensBefore, await ScalarAsync<long>("SELECT count(*) FROM user_token"));
    }

    // ------------------------------------------------------------------
    // Callers who may not
    // ------------------------------------------------------------------

    /// <summary>
    /// access-reviewer holds user.read but not user.resetpassword, so this
    /// proves the SPECIFIC permission is checked, not merely that a role is
    /// held.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_a_different_user_permission_is_refused()
    {
        var reviewer = await CallerAsync("access-reviewer");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(reviewer.UserId, subject.UserId));
    }

    [Fact]
    public async Task A_caller_holding_no_role_is_refused()
    {
        var caller = await CallerAsync(null);
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(caller.UserId, subject.UserId));
    }

    /// <summary>
    /// user.resetpassword is human-only. The caller here holds the permission,
    /// so the only thing that differs from the successful case is the actor
    /// type the context carries.
    /// </summary>
    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId, actorType: ActorType.Agent));
    }

    // ------------------------------------------------------------------

    private enum Scenario
    {
        InactiveUser,
        InactiveIdentity,
        NoLocalIdentity,
        TwoLocalIdentities,
        NoEmail,
        NoCredential,
        AgentUser,
    }

    private sealed record Subject(
        UserId UserId, Guid IdentityId, IReadOnlyList<Guid> IdentityIds, string? Email);

    private sealed record IssuedToken(Guid Id, string PlainText);

    private sealed record TokenRow(
        Guid Id, string TokenType, string Hash, Guid CreatedBy,
        object? UsedAt, object? InvalidatedAt, double LifetimeSeconds);

    private sealed record CredentialRow(
        string Hash, string ChangedAt, bool MustChangePassword,
        int FailedAttemptCount, bool IsLocked, string? LockedUntil);

    private sealed record NotificationRow(string Type, string Recipient);

    private sealed record RecordRow(
        string OriginKind, Guid? ActorUserId, string? Reason,
        string? Payload, string? MustChangePassword);

    /// <summary>Everything CRD-C5 could write, for one subject.</summary>
    private sealed record Footprint(
        long Tokens, long Invalidated, long Notifications, long AuditRefs, long FlaggedCredentials);

    /// <summary>
    /// The refusal, and the proof of it: the command throws the ordinary
    /// business-rule exception and every table it writes is exactly as it
    /// was. The prior live reset token is what makes a skipped check visible —
    /// an invalidation that ran before the refusal would change the footprint
    /// even though the transaction threw.
    /// </summary>
    private async Task AssertRefusedAsync(Subject subject, Func<Task> dispatch)
    {
        foreach (var identityId in subject.IdentityIds)
            await InsertTokenAsync(identityId, TokenType.PasswordReset);

        var before = await FootprintAsync(subject);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);

        Assert.Equal(before, await FootprintAsync(subject));
    }

    private async Task<Footprint> FootprintAsync(Subject subject)
    {
        var identities = subject.IdentityIds.ToArray();
        var everything = identities.Append(subject.UserId.Value).ToArray();

        return new Footprint(
            await ArrayScalarAsync(
                "SELECT count(*) FROM user_token WHERE user_identity_id = ANY(@ids)", identities),
            await ArrayScalarAsync(
                "SELECT count(*) FROM user_token WHERE user_identity_id = ANY(@ids) AND invalidated_at IS NOT NULL",
                identities),
            await ArrayScalarAsync(
                """
                SELECT count(*) FROM notification n
                JOIN user_token t ON t.id = n.token_id
                WHERE t.user_identity_id = ANY(@ids)
                """, identities),
            await ArrayScalarAsync(
                "SELECT count(*) FROM audit.audit_entity_ref WHERE entity_id = ANY(@ids)", everything),
            await ArrayScalarAsync(
                "SELECT count(*) FROM credential WHERE user_identity_id = ANY(@ids) AND must_change_password",
                identities));
    }

    private async Task<PermanentCaller> CallerAsync(string? roleCode)
        => await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, $"crd-c5-{roleCode ?? "unprivileged"}", roleCode);

    private async Task DispatchAsync(
        UserId caller,
        UserId target,
        string reason = Reason,
        ActorType actorType = ActorType.Human,
        List<string>? captured = null)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        // The plaintext's only exit is the notification declaration, and the
        // administrator never receives it. The end-to-end case still needs it,
        // so it is observed at the declaration — the same value the post-commit
        // sender would render — without any production seam.
        if (captured is not null)
        {
            services.AddScoped<INotificationEvents>(
                sp => new CapturingNotificationEvents(
                    sp.GetRequiredService<ScopedNotificationEvents>(), captured));
        }

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(
                caller,
                actorType,
                actorType == ActorType.Human
                    ? TestActorIdentity.Human()
                    : TestActorIdentity.NonHuman());

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<AdminResetPasswordCommand, AdminResetPasswordResult>(
                new AdminResetPasswordCommand(target, reason),
                CancellationToken.None);
    }

    private async Task ResetAsync(string token, string newPassword)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ResetPasswordCommand, ResetPasswordResult>(
                new ResetPasswordCommand(token, newPassword),
                CancellationToken.None);
    }

    private sealed class CapturingNotificationEvents(
        INotificationEvents inner, List<string> captured) : INotificationEvents
    {
        public void Emit(
            NotificationType notificationType,
            UserToken token,
            string recipient,
            string plaintextToken)
        {
            inner.Emit(notificationType, token, recipient, plaintextToken);
            captured.Add(plaintextToken);
        }
    }

    private async Task<Subject> SeedAsync(
        bool userActive = true,
        bool identityActive = true,
        int localIdentities = 1,
        bool withEmail = true,
        bool withCredential = true,
        bool lockedOut = false,
        string actorType = "Human")
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var email = withEmail ? $"target-{unique}@example.test" : null;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  deactivated_at, deactivated_by,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', '{actorType}', 'Reset', 'Target', 'Reset Target',
                  {(email is null ? "NULL" : $"'{email}'")},
                  '{(userActive ? "Active" : "Inactive")}',
                  {(userActive ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}', now(), '{system}')
             """);

        var identityIds = new List<Guid>();

        for (var i = 0; i < localIdentities; i++)
        {
            var identityId = Guid.NewGuid();
            identityIds.Add(identityId);

            await ExecuteAsync(
                $"""
                 INSERT INTO user_identity
                     (id, user_id, actor_type, identity_type, identity_provider,
                      subject_id, username, status, deactivated_at, deactivated_by,
                      created_at, created_by)
                 VALUES
                     ('{identityId}', '{userId}', '{actorType}', 'Local', 'Application',
                      '{identityId}', 'target-{i}-{unique}',
                      '{(identityActive ? "Active" : "Inactive")}',
                      {(identityActive ? "NULL, NULL" : $"now(), '{system}'")},
                      now() - interval '1 day', '{system}')
                 """);

            if (!withCredential)
                continue;

            var current = new PasswordHasher().Hash(Current);

            await ExecuteAsync(
                $"""
                 INSERT INTO credential
                     (id, user_identity_id, identity_type, password_hash,
                      password_algorithm, password_changed_at, must_change_password,
                      failed_attempt_count, locked_until, created_at, created_by)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', 'Local', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day', false,
                      {(lockedOut ? 5 : 0)},
                      {(lockedOut ? "now() + interval '1 hour'" : "NULL")},
                      now() - interval '1 day', '{system}');

                 INSERT INTO password_history
                     (id, user_identity_id, password_hash, password_algorithm, created_at)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day');
                 """);
        }

        return new Subject(
            new UserId(userId),
            identityIds.FirstOrDefault(),
            identityIds,
            email);
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
                    'CRD-C5 self-target test.'
             FROM role r WHERE r.code = '{roleCode}'
             """);

    private async Task<IssuedToken> InsertTokenAsync(
        Guid identityId, TokenType tokenType, bool used = false, bool expired = false)
    {
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{tokenId.Value}', '{identityId}', '{tokenType}', '{material.Hash}',
                  {(expired ? "now() - interval '1 minute'" : "now() + interval '1 hour'")},
                  {(used ? "now() - interval '5 minutes'" : "NULL")},
                  NULL, now() - interval '2 hours', '{User.SystemUserId.Value}')
             """);

        return new IssuedToken(tokenId.Value, material.PlainText);
    }

    private async Task<IReadOnlyList<TokenRow>> ReadTokensAsync(Guid identityId)
    {
        var rows = new List<TokenRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT id, token_type, token_hash, created_by, used_at::text, invalidated_at::text,
                   EXTRACT(EPOCH FROM (expires_at - created_at))::float8
            FROM user_token WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new TokenRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDouble(6)));
        }

        return rows;
    }

    private async Task<CredentialRow> ReadCredentialAsync(Guid identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_changed_at::text, must_change_password,
                   failed_attempt_count, locked_until IS NOT NULL, locked_until::text
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
            reader.GetInt32(3), reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private async Task<IReadOnlyList<NotificationRow>> ReadNotificationsAsync(Guid tokenId)
    {
        var rows = new List<NotificationRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT notification_type, recipient FROM notification WHERE token_id = @id",
            connection);

        command.Parameters.AddWithValue("id", tokenId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            rows.Add(new NotificationRow(reader.GetString(0), reader.GetString(1)));

        return rows;
    }

    private async Task<IReadOnlyList<RecordRow>> ReadRecordsAsync(Guid identityId, string eventType)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.origin_kind, r.actor_user_id, r.reason, r.payload::text,
                   r.payload::jsonb ->> 'mustChangePassword'
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type
              AND e.entity_type = 'Identity'
              AND e.entity_id = @id
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private async Task<long> CountRefsAsync(
        string eventType, string entityType, Guid entityId, string role)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type
              AND e.entity_type = @entityType
              AND e.entity_id = @id
              AND e.ref_role = @role
            """, connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("id", entityId);
        command.Parameters.AddWithValue("role", role);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<long> ArrayScalarAsync(string sql, Guid[] ids)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("ids", ids);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
