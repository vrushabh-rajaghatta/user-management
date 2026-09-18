using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.DeactivateUser;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.ReactivateUser;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-C4 / USR-C5 end to end, through the real pipeline and a real database
/// (docs/requirements.md, "USR-C4 / USR-C5", V1–V8, R1–R5, K1–K2).
///
/// Every row the cascade could touch is seeded in a state that tests one rule:
/// an active, a future, an ended and an already-revoked assignment; sessions
/// live, already revoked, and belonging to someone else; tokens outstanding and
/// already used; an identity active, and one already deactivated for another
/// reason. After one command, each must be exactly what the contract says.
///
/// Its own provisioned database: the administrator becomes the actor of audit
/// records, which can never be deleted.
/// </summary>
public sealed class UserLifecycleIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Reason = "Left the company; HR ticket 4471.";

    private readonly ActivationDatabase _database;

    public UserLifecycleIntegrationTests(ActivationDatabase database) => _database = database;

    // ================================================================ USR-C4

    /// <summary>V3, V4, V5, V6 — the whole cascade, row by row.</summary>
    [Fact]
    public async Task Deactivation_performs_the_whole_cascade()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var bystander = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);

        var user = await RowAsync(
            "SELECT status, deactivated_at, deactivated_by FROM app_user WHERE id = @id", target.UserId.Value);

        Assert.Equal("Inactive", user[0]);
        var at = (DateTime)user[1]!;
        Assert.Equal(admin.Value, user[2]);

        // V3 — every identity that was active carries the user's stamp; the
        // one already inactive keeps its own.
        foreach (var identity in new[] { target.Primary, target.Secondary })
        {
            var row = await RowAsync(
                "SELECT status, deactivated_at, deactivated_by FROM user_identity WHERE id = @id", identity);

            Assert.Equal("Inactive", row[0]);
            Assert.Equal(at, row[1]);
            Assert.Equal(admin.Value, row[2]);
        }

        var dormant = await RowAsync(
            "SELECT status, deactivated_at, deactivated_by FROM user_identity WHERE id = @id", target.Dormant);

        Assert.Equal("Inactive", dormant[0]);
        Assert.Equal(DormantStampAt, DateTime.SpecifyKind((DateTime)dormant[1]!, DateTimeKind.Utc));
        Assert.Equal(User.SystemUserId.Value, dormant[2]);

        // V4 — the AUT-C2 rule, attributed to System.
        var active = await AssignmentAsync(target.ActiveAssignment);
        Assert.Equal(at, active.EffectiveTo);
        Assert.Equal(at, active.RevokedAt);
        Assert.Equal(User.SystemUserId.Value, active.RevokedBy);
        Assert.Equal("User deactivated", active.RevocationReason);

        var future = await AssignmentAsync(target.FutureAssignment);
        Assert.Equal(future.EffectiveFrom, future.EffectiveTo);
        Assert.Equal(User.SystemUserId.Value, future.RevokedBy);
        Assert.Equal("User deactivated", future.RevocationReason);

        var ended = await AssignmentAsync(target.EndedAssignment);
        Assert.Null(ended.RevokedAt);
        Assert.Equal(EndedTo, ended.EffectiveTo);

        var revoked = await AssignmentAsync(target.RevokedAssignment);
        Assert.Equal("Earlier revocation.", revoked.RevocationReason);
        Assert.Equal(admin.Value, revoked.RevokedBy);

        // V5 — every live session, both identities; the earlier revocation and
        // someone else's session untouched.
        foreach (var session in new[] { target.PrimarySession, target.SecondarySession })
        {
            var row = await RowAsync(
                "SELECT revoked_at, revoked_by, revocation_reason FROM user_session WHERE id = @id", session);

            Assert.Equal(at, row[0]);
            Assert.Equal(User.SystemUserId.Value, row[1]);
            Assert.Equal("UserDeactivated", row[2]);
        }

        Assert.Equal("Logout", (await RowAsync(
            "SELECT revocation_reason FROM user_session WHERE id = @id", target.EndedSession))[0]);

        Assert.Equal(DBNull.Value, (await RowAsync(
            "SELECT revoked_at FROM user_session WHERE id = @id", bystander.PrimarySession))[0]);

        // V6 — outstanding tokens invalidated at the same instant; the used one
        // is not rewritten; someone else's is untouched.
        foreach (var token in new[] { target.ResetToken, target.ActivationToken })
            Assert.Equal(at, (await RowAsync("SELECT invalidated_at FROM user_token WHERE id = @id", token))[0]);

        Assert.Equal(DBNull.Value, (await RowAsync(
            "SELECT invalidated_at FROM user_token WHERE id = @id", target.UsedToken))[0]);

        Assert.Equal(DBNull.Value, (await RowAsync(
            "SELECT invalidated_at FROM user_token WHERE id = @id", bystander.ResetToken))[0]);
    }

    /// <summary>V7 — one operation, told as one story, caused by UserDeactivated.</summary>
    [Fact]
    public async Task Deactivation_is_audited_as_one_operation_caused_by_UserDeactivated()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);

        var root = await RowAsync(
            """
            SELECT audit_id, operation_id, reason, actor_user_id, before::text, after::text
              FROM audit.audit_record
             WHERE event_type = 'UserDeactivated' AND entity_id = @id
            """, target.UserId.Value);

        var rootId = (Guid)root[0]!;
        var operation = (Guid)root[1]!;

        Assert.Equal(Reason, root[2]);
        Assert.Equal(admin.Value, root[3]);
        Assert.Contains("\"Status\": \"Active\"", (string)root[4]!);
        Assert.Contains("\"Status\": \"Inactive\"", (string)root[5]!);

        var records = await RowsAsync(
            """
            SELECT event_type, causation_id, reason, actor_user_id
              FROM audit.audit_record
             WHERE operation_id = @id
             ORDER BY sequence
            """, operation);

        // Two identities, two assignments (active and future), two sessions,
        // and the root: no token record, nothing for the ended or revoked rows.
        Assert.Equal(
            ["IdentityDeactivated", "IdentityDeactivated", "RoleRevoked", "RoleRevoked",
             "SessionRevoked", "SessionRevoked", "UserDeactivated"],
            records.Select(x => (string)x[0]!).Order(StringComparer.Ordinal));

        foreach (var record in records.Where(x => (string)x[0]! != "UserDeactivated"))
        {
            Assert.Equal(rootId, record[1]);
            Assert.Equal(Reason, record[2]);
            Assert.Equal(admin.Value, record[3]);
        }

        Assert.DoesNotContain(records, x => (string)x[0]! == "TokenInvalidated");
    }

    /// <summary>V2 — a user who never activated can still be taken out.</summary>
    [Fact]
    public async Task A_pending_user_can_be_deactivated()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync(pending: true);

        await DeactivateAsync(admin, target.UserId);

        Assert.Equal("Inactive", (await RowAsync("SELECT status FROM app_user WHERE id = @id", target.UserId.Value))[0]);
        Assert.NotEqual(DBNull.Value, (await RowAsync(
            "SELECT invalidated_at FROM user_token WHERE id = @id", target.ActivationToken))[0]);
    }

    /// <summary>V1 — each refusal writes nothing, and says why.</summary>
    [Fact]
    public async Task Deactivation_refusals_write_nothing()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);
        var records = await AuditCountAsync();

        await AssertRefusedAsync(() => DeactivateAsync(admin, target.UserId), "The user is already inactive.");
        await AssertRefusedAsync(() => DeactivateAsync(admin, admin), "A user cannot deactivate themselves.");
        await AssertRefusedAsync(() => DeactivateAsync(admin, User.SystemUserId), "This user cannot be deactivated.");
        await AssertRefusedAsync(() => DeactivateAsync(admin, UserId.New()), "The user does not exist.");

        Assert.Equal(records, await AuditCountAsync());
        Assert.Equal("Active", (await RowAsync("SELECT status FROM app_user WHERE id = @id", admin.Value))[0]);
    }

    /// <summary>
    /// V8 — partial execution is the failure the one transaction prevents. The
    /// fault is injected at the last write step, after roles, sessions,
    /// identities and the user were changed in memory and flushed.
    /// </summary>
    [Fact]
    public async Task A_fault_during_the_cascade_leaves_everything_as_it_was()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var records = await AuditCountAsync();

        // The injected fault's own message: any other InvalidOperationException
        // (a missing handler, say) would prove nothing about the rollback.
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(
            admin, target.UserId,
            services =>
            {
                services.RemoveAll<IUserTokenRepository>();
                services.AddScoped<IUserTokenRepository, FailingTokenRepository>();
            }));

        Assert.Equal("Injected fault at the token step.", fault.Message);

        Assert.Equal("Active", (await RowAsync("SELECT status FROM app_user WHERE id = @id", target.UserId.Value))[0]);
        Assert.Equal("Active", (await RowAsync("SELECT status FROM user_identity WHERE id = @id", target.Primary))[0]);
        Assert.Null((await AssignmentAsync(target.ActiveAssignment)).RevokedAt);
        Assert.Equal(DBNull.Value, (await RowAsync("SELECT revoked_at FROM user_session WHERE id = @id", target.PrimarySession))[0]);
        Assert.Equal(DBNull.Value, (await RowAsync("SELECT invalidated_at FROM user_token WHERE id = @id", target.ResetToken))[0]);
        Assert.Equal(records, await AuditCountAsync());
    }

    // ================================================================ USR-C5

    /// <summary>R2, R3, R4 — returns the user and their stamped identities, and nothing else.</summary>
    [Fact]
    public async Task Reactivation_restores_the_user_and_nothing_else()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);
        await ReactivateAsync(admin, target.UserId);

        var user = await RowAsync("SELECT status, deactivated_at, deactivated_by FROM app_user WHERE id = @id", target.UserId.Value);
        Assert.Equal("Active", user[0]);
        Assert.Equal(DBNull.Value, user[1]);
        Assert.Equal(DBNull.Value, user[2]);

        foreach (var identity in new[] { target.Primary, target.Secondary })
            Assert.Equal("Active", (await RowAsync("SELECT status FROM user_identity WHERE id = @id", identity))[0]);

        // R2 — deactivated for another reason, before; it stays so.
        Assert.Equal("Inactive", (await RowAsync("SELECT status FROM user_identity WHERE id = @id", target.Dormant))[0]);

        // R3 — nothing restored.
        Assert.Equal(0L, await ScalarAsync<long>(
            """
            SELECT count(*) FROM user_role
             WHERE user_id = @id AND revoked_at IS NULL
               AND (effective_to IS NULL OR effective_to > now())
            """, target.UserId.Value));

        Assert.NotEqual(DBNull.Value, (await RowAsync("SELECT revoked_at FROM user_session WHERE id = @id", target.PrimarySession))[0]);
        Assert.NotEqual(DBNull.Value, (await RowAsync("SELECT invalidated_at FROM user_token WHERE id = @id", target.ResetToken))[0]);

        // R4 — one UserReactivated, one IdentityReactivated per identity returned.
        var root = await RowAsync(
            "SELECT audit_id, operation_id FROM audit.audit_record WHERE event_type = 'UserReactivated' AND entity_id = @id",
            target.UserId.Value);

        var records = await RowsAsync(
            "SELECT event_type, causation_id, entity_id FROM audit.audit_record WHERE operation_id = @id ORDER BY sequence",
            (Guid)root[1]!);

        Assert.Equal(
            ["IdentityReactivated", "IdentityReactivated", "UserReactivated"],
            records.Select(x => (string)x[0]!).Order(StringComparer.Ordinal));

        Assert.All(records.Where(x => (string)x[0]! == "IdentityReactivated"), x => Assert.Equal(root[0], x[1]));
        Assert.DoesNotContain(records, x => (Guid)x[2]! == target.Dormant);
    }

    /// <summary>R1 — each refusal writes nothing.</summary>
    [Fact]
    public async Task Reactivation_refusals_write_nothing()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var records = await AuditCountAsync();

        await AssertRefusedAsync(() => ReactivateAsync(admin, target.UserId), "The user is already active.");
        await AssertRefusedAsync(() => ReactivateAsync(admin, User.SystemUserId), "This user cannot be reactivated.");
        await AssertRefusedAsync(() => ReactivateAsync(admin, UserId.New()), "The user does not exist.");

        Assert.Equal(records, await AuditCountAsync());
    }

    /// <summary>
    /// R1 — the address was given to someone else while this user was away. The
    /// pre-check gives the message; the unique index would refuse it anyway.
    /// </summary>
    [Fact]
    public async Task Reactivation_is_refused_when_the_email_is_now_held_by_someone_else()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);

        // A new joiner takes the address, in a different case (AU3).
        await SeedTargetAsync(email: target.Email.ToUpperInvariant());

        var records = await AuditCountAsync();

        await AssertRefusedAsync(
            () => ReactivateAsync(admin, target.UserId),
            "Another active user now has this user's email address.");

        Assert.Equal("Inactive", (await RowAsync("SELECT status FROM app_user WHERE id = @id", target.UserId.Value))[0]);
        Assert.Equal(records, await AuditCountAsync());
    }

    /// <summary>R5 — after the round trip the user can be granted access afresh.</summary>
    [Fact]
    public async Task After_the_round_trip_access_is_granted_afresh()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        await DeactivateAsync(admin, target.UserId);
        await ReactivateAsync(admin, target.UserId);

        await GrantAsync(await SecurityAdminAsync(), target.UserId);

        Assert.Equal(1L, await ScalarAsync<long>(
            "SELECT count(*) FROM user_role WHERE user_id = @id AND revoked_at IS NULL AND effective_to IS NULL",
            target.UserId.Value));
    }

    // ================================================================ D6 — the lock

    /// <summary>
    /// K2 — GrantRole takes the target's row lock BEFORE its status check.
    /// Another transaction holds the lock and deactivates the user; the grant
    /// must wait, then see Inactive and be refused. A grant that checked first
    /// would have read Active and committed a live assignment.
    /// </summary>
    [Fact]
    public async Task A_grant_waits_for_the_users_lock_and_then_sees_the_deactivation()
    {
        var securityAdmin = await SecurityAdminAsync();
        var target = await SeedTargetAsync();

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await ExecuteAsync(holder, transaction, "SELECT 1 FROM app_user WHERE id = @id FOR UPDATE", target.UserId.Value);

        var grant = Task.Run(() => GrantAsync(securityAdmin, target.UserId));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(grant.IsCompleted, "the grant did not wait for the user's row lock");

        await ExecuteAsync(holder, transaction,
            """
            UPDATE app_user SET status = 'Inactive', deactivated_at = now(), deactivated_by = @system WHERE id = @id
            """, target.UserId.Value);

        await transaction.CommitAsync();

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => grant);
        Assert.Equal("A role cannot be granted to this user.", refusal.Message);
    }

    /// <summary>
    /// K1, the other order — deactivation takes the lock before reading the
    /// assignments, so one committed while it waited is revoked with the rest.
    ///
    /// clock_timestamp(), not now(): the assignment is made at the real moment
    /// of the insert, AFTER the deactivation began waiting — as a racing grant
    /// would be. A deactivation that read its clock before the lock would then
    /// date the revocation before the assignment, and UserRole.Revoke refuses
    /// that.
    /// </summary>
    [Fact]
    public async Task A_deactivation_waits_for_the_users_lock_and_then_revokes_what_was_granted()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var late = Guid.NewGuid();

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await ExecuteAsync(holder, transaction, "SELECT 1 FROM app_user WHERE id = @id FOR UPDATE", target.UserId.Value);

        var deactivation = Task.Run(() => DeactivateAsync(admin, target.UserId));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(deactivation.IsCompleted, "the deactivation did not wait for the user's row lock");

        await ExecuteAsync(holder, transaction,
            $"""
            INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                   effective_from, effective_to, assigned_at, assigned_by, assignment_reason)
            SELECT '{late}', @id, 'Human', r.id, 'Global', NULL, clock_timestamp(), NULL, clock_timestamp(), @system, 'Granted while deactivation waited.'
              FROM role r WHERE r.code = 'security-administrator'
            """, target.UserId.Value);

        await transaction.CommitAsync();
        await deactivation;

        var granted = await AssignmentAsync(late);
        Assert.NotNull(granted.RevokedAt);
        Assert.Equal("User deactivated", granted.RevocationReason);
    }

    // ================================================================ harness

    private static readonly DateTime DormantStampAt = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndedTo = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Target(
        UserId UserId,
        string Email,
        Guid Primary,
        Guid Secondary,
        Guid Dormant,
        Guid ActiveAssignment,
        Guid FutureAssignment,
        Guid EndedAssignment,
        Guid RevokedAssignment,
        Guid PrimarySession,
        Guid SecondarySession,
        Guid EndedSession,
        Guid ResetToken,
        Guid ActivationToken,
        Guid UsedToken);

    private sealed record Assignment(DateTime EffectiveFrom, DateTime? EffectiveTo, DateTime? RevokedAt, Guid? RevokedBy, string? RevocationReason);

    private sealed class FailingTokenRepository : IUserTokenRepository
    {
        public Task AddAsync(UserToken token, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserIdentityId?> TryConsumeAsync(UserTokenId tokenId, string tokenHash, TokenType expected, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<UserTokenId>> InvalidatePriorAsync(UserIdentityId identityId, TokenType tokenType, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> InvalidateOutstandingForUserAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Injected fault at the token step.");
    }

    private Task DeactivateAsync(UserId caller, UserId target, Action<IServiceCollection>? configure = null)
        => DispatchAsync<DeactivateUserCommand, DeactivateUserResult>(caller, new DeactivateUserCommand(target, Reason), configure);

    private Task ReactivateAsync(UserId caller, UserId target)
        => DispatchAsync<ReactivateUserCommand, ReactivateUserResult>(caller, new ReactivateUserCommand(target, "Rehired into QA."));

    private async Task GrantAsync(UserId caller, UserId target)
        => await DispatchAsync<GrantRoleCommand, GrantRoleResult>(
            caller,
            new GrantRoleCommand(target, await RoleIdAsync("access-reviewer"), null, null, "Access after return."));

    private async Task<TResult> DispatchAsync<TCommand, TResult>(
        UserId caller, TCommand command, Action<IServiceCollection>? configure = null)
        where TCommand : ICommand<TResult>
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        configure?.Invoke(services);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<TCommand, TResult>(command, CancellationToken.None);
    }

    private static async Task AssertRefusedAsync(Func<Task> dispatch, string message)
    {
        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);
        Assert.Equal(message, refusal.Message);
    }

    private async Task<UserId> AdminAsync()
        => (await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "usr-c4-user-administrator", "user-administrator")).UserId;

    private async Task<UserId> SecurityAdminAsync()
        => (await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "usr-c4-security-administrator", "security-administrator")).UserId;

    /// <summary>
    /// A human with two active local identities and one deactivated earlier by
    /// someone else; four assignments, one per state; sessions live, ended, and
    /// on the second identity; tokens outstanding and used. A pending user has
    /// no credential and an outstanding activation token.
    /// </summary>
    private async Task<Target> SeedTargetAsync(bool pending = false, string? email = null)
    {
        var userId = Guid.NewGuid();
        var unique = userId.ToString("N");
        email ??= $"lifecycle-{unique}@example.test";

        var primary = Guid.NewGuid();
        var secondary = Guid.NewGuid();
        var dormant = Guid.NewGuid();
        var active = Guid.NewGuid();
        var future = Guid.NewGuid();
        var ended = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        var primarySession = Guid.NewGuid();
        var secondarySession = Guid.NewGuid();
        var endedSession = Guid.NewGuid();
        var resetToken = Guid.NewGuid();
        var activationToken = Guid.NewGuid();
        var usedToken = Guid.NewGuid();

        var admin = await AdminAsync();
        var system = User.SystemUserId.Value;

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'John', 'Leaver', 'John Leaver {unique[..8]}', '{email}',
                     'Active', now() - interval '1 year', '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by,
                                        deactivated_at, deactivated_by)
             VALUES ('{primary}', '{userId}', 'Human', 'Local', 'Application', '{primary}',
                     'lc-a-{unique[..20]}', 'Active', now() - interval '1 year', '{system}', NULL, NULL),
                    ('{secondary}', '{userId}', 'Human', 'Local', 'Application', '{secondary}',
                     'lc-b-{unique[..20]}', 'Active', now() - interval '1 year', '{system}', NULL, NULL),
                    ('{dormant}', '{userId}', 'Human', 'Local', 'Application', '{dormant}',
                     'lc-c-{unique[..20]}', 'Inactive', now() - interval '1 year', '{system}',
                     '{DormantStampAt:yyyy-MM-dd HH:mm:ss}+00', '{system}');

             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                                    revoked_at, revoked_by, revocation_reason)
             SELECT x.id::uuid, '{userId}', 'Human', r.id, 'Global', NULL,
                    x.effective_from, x.effective_to, x.assigned_at, '{admin.Value}', 'Seeded for lifecycle tests.',
                    x.revoked_at, x.revoked_by::uuid, x.revocation_reason
               FROM (VALUES
                      ('{active}',  'access-reviewer',        now() - interval '30 days', NULL::timestamptz,
                       now() - interval '30 days', NULL::timestamptz, NULL, NULL),
                      ('{future}',  'user-administrator',     now() + interval '10 days', NULL::timestamptz,
                       now(), NULL::timestamptz, NULL, NULL),
                      ('{ended}',   'security-administrator', '2026-07-01 00:00:00+00'::timestamptz, '{EndedTo:yyyy-MM-dd HH:mm:ss}+00'::timestamptz,
                       '2026-06-30 00:00:00+00'::timestamptz, NULL::timestamptz, NULL, NULL),
                      ('{revoked}', 'access-reviewer',        now() - interval '90 days', now() - interval '60 days',
                       now() - interval '90 days', now() - interval '60 days', '{admin.Value}', 'Earlier revocation.')
                    ) AS x(id, code, effective_from, effective_to, assigned_at, revoked_at, revoked_by, revocation_reason)
               JOIN role r ON r.code = x.code;

             INSERT INTO user_session (id, user_identity_id, created_at, last_activity_at, expires_at,
                                       revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
             VALUES ('{primarySession}', '{primary}', now() - interval '5 minutes', now(), now() + interval '8 hours',
                     NULL, NULL, NULL, NULL, 'lifecycle-tests/1.0'),
                    ('{secondarySession}', '{secondary}', now() - interval '5 minutes', now(), now() + interval '8 hours',
                     NULL, NULL, NULL, NULL, 'lifecycle-tests/1.0'),
                    ('{endedSession}', '{primary}', now() - interval '2 days', now() - interval '2 days', now() + interval '8 hours',
                     now() - interval '2 days', '{userId}', 'Logout', NULL, 'lifecycle-tests/1.0');

             INSERT INTO user_token (id, user_identity_id, token_type, token_hash, expires_at,
                                     used_at, invalidated_at, created_at, created_by)
             VALUES ('{resetToken}', '{primary}', 'PasswordReset', 'reset-{unique}', now() + interval '1 hour',
                     NULL, NULL, now(), '{system}'),
                    ('{activationToken}', '{secondary}', 'Activation', 'activation-{unique}', now() + interval '72 hours',
                     NULL, NULL, now(), '{system}'),
                    ('{usedToken}', '{primary}', 'Activation', 'used-{unique}', now() + interval '72 hours',
                     now() - interval '300 days', NULL, now() - interval '301 days', '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        if (!pending)
        {
            await using var credential = new NpgsqlCommand(
                $"""
                 INSERT INTO credential (id, user_identity_id, identity_type, password_hash, password_algorithm, password_changed_at,
                                         failed_attempt_count, must_change_password, created_at, created_by)
                 VALUES ('{Guid.NewGuid()}', '{primary}', 'Local', 'not-a-real-hash', 'PBKDF2-SHA256', now() - interval '300 days',
                         0, false, now() - interval '300 days', '{system}');
                 """, connection);

            await credential.ExecuteNonQueryAsync();
        }

        return new Target(
            new UserId(userId), email, primary, secondary, dormant,
            active, future, ended, revoked,
            primarySession, secondarySession, endedSession,
            resetToken, activationToken, usedToken);
    }

    private async Task<Assignment> AssignmentAsync(Guid id)
    {
        var row = await RowAsync(
            "SELECT effective_from, effective_to, revoked_at, revoked_by, revocation_reason FROM user_role WHERE id = @id", id);

        return new Assignment(
            (DateTime)row[0]!,
            row[1] as DateTime?,
            row[2] as DateTime?,
            row[3] as Guid?,
            row[4] as string);
    }

    private async Task<RoleId> RoleIdAsync(string code)
        => new(await ScalarAsync<Guid>("SELECT id FROM role WHERE code = @id", code));

    private Task<long> AuditCountAsync()
        => ScalarAsync<long>("SELECT count(*) FROM audit.audit_record WHERE @id::text IS NOT NULL", "any");

    private async Task<T> ScalarAsync<T>(string sql, object id)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task<object?[]> RowAsync(string sql, object id)
        => Assert.Single(await RowsAsync(sql, id));

    private async Task<List<object?[]>> RowsAsync(string sql, object id)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();

        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            rows.Add(values);
        }

        return rows;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);
        await command.ExecuteNonQueryAsync();
    }
}
