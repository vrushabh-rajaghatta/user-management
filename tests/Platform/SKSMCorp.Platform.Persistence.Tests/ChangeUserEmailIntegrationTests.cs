using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Commands.ActivateAccount;
using SKSMCorp.Platform.Application.Users.Commands.ChangeUserEmail;
using SKSMCorp.Platform.Application.Users.Commands.DeactivateUser;
using SKSMCorp.Platform.Application.Users.Commands.ReactivateUser;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Services;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// USR-C3 ChangeUserEmail end to end, through the real pipeline and a real
/// database (docs/requirements.md, "USR-C3 ChangeUserEmail", CE-A1 to CE-A10).
///
/// An administrator changes a human's address: immediately, audited, under the
/// target's row lock, and every link already sent to the old address stops
/// working — with nothing issued in its place (CE7).
///
/// Its own provisioned database: the administrators become the actors of audit
/// records, which can never be deleted.
/// </summary>
public sealed class ChangeUserEmailIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Reason = "Married; HR ticket 5120.";

    private const string Taken = "A user with this email address already exists.";

    private readonly ActivationDatabase _database;

    public ChangeUserEmailIntegrationTests(ActivationDatabase database) => _database = database;

    // ---------------------------------------------------------------- CE-A1

    [Fact]
    public async Task A_change_stores_the_trimmed_address_and_records_it_once()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var address = $"New.{Guid.NewGuid():N}@Example.test";

        await ChangeAsync(admin, target.UserId, $"  {address}  ", Reason);

        Assert.Equal(address, await EmailAsync(target.UserId));

        var record = await RowAsync(
            """
            SELECT before::text, after::text, reason, actor_user_id
              FROM audit.audit_record
             WHERE event_type = 'UserEmailChanged' AND entity_id = @id
            """, target.UserId.Value);

        Assert.Equal([("Email", target.Email)], Members((string)record[0]!));
        Assert.Equal([("Email", address)], Members((string)record[1]!));
        Assert.Equal(Reason, record[2]);
        Assert.Equal(admin.Value, record[3]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_reason_the_record_has_none(string? reason)
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();

        await ChangeAsync(admin, target.UserId, $"noreason-{Guid.NewGuid():N}@example.test", reason);

        Assert.Equal(DBNull.Value, await ScalarAsync<object>(
            "SELECT reason FROM audit.audit_record WHERE event_type = 'UserEmailChanged' AND entity_id = @id",
            target.UserId.Value));
    }

    // ---------------------------------------------------------------- CE-A2

    [Fact]
    public async Task The_same_address_in_any_case_changes_nothing_at_all()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var before = await SnapshotAsync(target.UserId);

        await ChangeAsync(admin, target.UserId, target.Email, Reason);
        await ChangeAsync(admin, target.UserId, target.Email.ToUpperInvariant(), Reason);

        Assert.Equal(before, await SnapshotAsync(target.UserId));
        Assert.Equal(0L, await RecordsAsync(target.UserId));
    }

    // ---------------------------------------------------------------- CE-A3

    [Fact]
    public async Task An_address_another_active_human_holds_is_refused_in_any_case()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var holder = await SeedAsync();
        var before = await SnapshotAsync(target.UserId);

        Assert.Equal(Taken, await RefusalAsync(() => ChangeAsync(admin, target.UserId, holder.Email.ToUpperInvariant(), Reason)));

        Assert.Equal(before, await SnapshotAsync(target.UserId));
        Assert.Equal(0L, await RecordsAsync(target.UserId));
    }

    [Fact]
    public async Task An_address_held_only_by_an_inactive_human_is_available()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var departed = await SeedAsync(inactive: true);

        await ChangeAsync(admin, target.UserId, departed.Email, Reason);

        Assert.Equal(departed.Email, await EmailAsync(target.UserId));
    }

    [Fact]
    public async Task An_inactive_target_is_refused_an_address_an_active_human_holds()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync(inactive: true);
        var holder = await SeedAsync();

        Assert.Equal(Taken, await RefusalAsync(() => ChangeAsync(admin, target.UserId, holder.Email, Reason)));
        Assert.Equal(target.Email, await EmailAsync(target.UserId));
    }

    // ---------------------------------------------------------------- CE-A4

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two@@example.test")]
    [InlineData("nodot@example")]
    [InlineData("   ")]
    public async Task An_invalid_address_is_refused_and_nothing_is_written(string email)
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var before = await SnapshotAsync(target.UserId);

        Assert.Equal("Email address has an invalid format.", await RefusalAsync(() => ChangeAsync(admin, target.UserId, email, Reason)));

        Assert.Equal(before, await SnapshotAsync(target.UserId));
        Assert.Equal(0L, await RecordsAsync(target.UserId));
    }

    // ---------------------------------------------------------------- CE-A5

    [Fact]
    public async Task An_unknown_user_the_System_actor_and_an_agent_are_refused()
    {
        var admin = await AdminAsync();
        var agent = await SeedAgentAsync();
        var address = $"refused-{Guid.NewGuid():N}@example.test";

        Assert.Equal("The user does not exist.", await RefusalAsync(() => ChangeAsync(admin, UserId.New(), address, null)));
        Assert.Equal("This user's email cannot be changed.", await RefusalAsync(() => ChangeAsync(admin, User.SystemUserId, address, null)));
        Assert.Equal("This user's email cannot be changed.", await RefusalAsync(() => ChangeAsync(admin, agent, address, null)));

        Assert.Equal(0L, await RecordsAsync(agent));
    }

    [Fact]
    public async Task A_caller_without_user_update_is_refused()
    {
        var reviewer = await CallerAsync("usr-c3-access-reviewer", "access-reviewer");
        var target = await SeedAsync();

        var refusal = await RefusalAsync(() => ChangeAsync(reviewer, target.UserId, $"x-{Guid.NewGuid():N}@example.test", null));

        Assert.Contains("permission", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(target.Email, await EmailAsync(target.UserId));
    }

    // ---------------------------------------------------------------- CE-A6

    [Fact]
    public async Task Open_tokens_are_invalidated_at_one_instant_and_nothing_replaces_them()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var tokens = await ScalarAsync<long>(TokenCount, target.UserId.Value);
        var notifications = await ScalarAsync<long>("SELECT count(*) FROM notification WHERE @id::text IS NOT NULL", "any");

        await ChangeAsync(admin, target.UserId, $"moved-{Guid.NewGuid():N}@example.test", Reason);

        var reset = await InvalidatedAtAsync(target.ResetToken);
        var activation = await InvalidatedAtAsync(target.ActivationToken);

        Assert.NotNull(reset);
        Assert.Equal(reset, activation);
        Assert.Null(await InvalidatedAtAsync(target.UsedToken));

        // Nothing issued, nothing sent, and no fabricated per-token record (D13).
        Assert.Equal(tokens, await ScalarAsync<long>(TokenCount, target.UserId.Value));
        Assert.Equal(notifications, await ScalarAsync<long>("SELECT count(*) FROM notification WHERE @id::text IS NOT NULL", "any"));
        Assert.Equal(0L, await ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_record WHERE event_type = 'TokenInvalidated' AND entity_id = ANY(@id)",
            new[] { target.ResetToken, target.ActivationToken }));
    }

    [Fact]
    public async Task The_old_activation_link_no_longer_activates_the_account()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync(pending: true);

        await ChangeAsync(admin, target.UserId, $"typo-fixed-{Guid.NewGuid():N}@example.test", "Typo in the address.");

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => ActivateAsync(target.ActivationPlainText!));

        Assert.Equal("The activation token is not valid.", refusal.Message);
        Assert.Equal(0L, await ScalarAsync<long>(
            "SELECT count(*) FROM credential c JOIN user_identity i ON i.id = c.user_identity_id WHERE i.user_id = @id",
            target.UserId.Value));
    }

    // ---------------------------------------------------------------- CE-A7

    /// <summary>
    /// The lock is taken BEFORE anything about the user is read (CE9). Another
    /// transaction holds it and, while the command waits, sets the very address
    /// the command is about to send. A command that read first would see the
    /// old address and write a change — and a record whose Before is stale. One
    /// that locked first sees its own address already there: no change, no
    /// record, no token touched (CE6).
    /// </summary>
    [Fact]
    public async Task The_change_waits_for_the_users_row_lock_and_then_reads()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var address = $"locked-{Guid.NewGuid():N}@example.test";

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand("SELECT 1 FROM app_user WHERE id = @id FOR UPDATE", holder, transaction))
        {
            command.Parameters.AddWithValue("id", target.UserId.Value);
            await command.ExecuteNonQueryAsync();
        }

        var change = Task.Run(() => ChangeAsync(admin, target.UserId, address, Reason));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(change.IsCompleted, "the change did not wait for the user's row lock");

        await using (var command = new NpgsqlCommand("UPDATE app_user SET email = @email WHERE id = @id", holder, transaction))
        {
            command.Parameters.AddWithValue("email", address);
            command.Parameters.AddWithValue("id", target.UserId.Value);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await change;

        Assert.Equal(0L, await RecordsAsync(target.UserId));
        Assert.Null(await InvalidatedAtAsync(target.ResetToken));
    }

    /// <summary>
    /// The clock is read AFTER the lock (CE9): the instant stamped on the
    /// invalidated tokens is this command's turn, not the moment it began to
    /// wait. Both instants are the application's clock.
    /// </summary>
    [Fact]
    public async Task The_tokens_are_stamped_after_the_wait_not_before_it()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand("SELECT 1 FROM app_user WHERE id = @id FOR UPDATE", holder, transaction))
        {
            command.Parameters.AddWithValue("id", target.UserId.Value);
            await command.ExecuteNonQueryAsync();
        }

        var change = Task.Run(() => ChangeAsync(admin, target.UserId, $"late-{Guid.NewGuid():N}@example.test", Reason));

        await Task.Delay(TimeSpan.FromMilliseconds(750));

        var released = DateTime.UtcNow;
        await transaction.CommitAsync();
        await change;

        var stamped = await InvalidatedAtAsync(target.ResetToken);

        Assert.NotNull(stamped);
        Assert.True(
            stamped.Value.ToUniversalTime() >= released.AddMilliseconds(-100),
            $"the tokens were stamped at {stamped:O}, before the lock was released at {released:O}");
    }

    // ---------------------------------------------------------------- CE-A8

    [Fact]
    public async Task Nothing_but_the_email_and_the_open_tokens_changes()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();
        var before = await EverythingElseAsync(target.UserId);

        await ChangeAsync(admin, target.UserId, $"only-{Guid.NewGuid():N}@example.test", Reason);

        Assert.Equal(before, await EverythingElseAsync(target.UserId));
    }

    // ---------------------------------------------------------------- CE-A9

    /// <summary>The USR-C5 refusal's remedy: change the returning user's address, then reactivate.</summary>
    [Fact]
    public async Task Changing_an_inactive_users_address_lets_them_be_reactivated()
    {
        var admin = await AdminAsync();
        var target = await SeedAsync();

        await DispatchAsync<DeactivateUserCommand, DeactivateUserResult>(
            admin, new DeactivateUserCommand(target.UserId, "Left the company."));

        await SeedAsync(email: target.Email.ToUpperInvariant());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => ReactivateAsync(admin, target.UserId));

        await ChangeAsync(admin, target.UserId, $"returning-{Guid.NewGuid():N}@example.test", "Returning; new address.");
        await ReactivateAsync(admin, target.UserId);

        Assert.Equal("Active", await ScalarAsync<string>("SELECT status FROM app_user WHERE id = @id", target.UserId.Value));
    }

    // ---------------------------------------------------------------- CE-A10

    /// <summary>CE2: no rule about who the target is. The record names the administrator as actor and subject.</summary>
    [Fact]
    public async Task An_administrators_change_to_their_own_record_is_an_administrator_change()
    {
        var self = await CallerAsync($"usr-c3-self-{Guid.NewGuid():N}"[..28], "user-administrator");
        var address = $"self-{Guid.NewGuid():N}@example.test";

        await ChangeAsync(self, self, address, null);

        Assert.Equal(address, await EmailAsync(self));
        Assert.Equal(self.Value, await ScalarAsync<Guid>(
            "SELECT actor_user_id FROM audit.audit_record WHERE event_type = 'UserEmailChanged' AND entity_id = @id",
            self.Value));
    }

    // ================================================================ harness

    private const string TokenCount =
        "SELECT count(*) FROM user_token t JOIN user_identity i ON i.id = t.user_identity_id WHERE i.user_id = @id";

    private sealed record Target(
        UserId UserId,
        string Email,
        Guid ResetToken,
        Guid ActivationToken,
        Guid UsedToken,
        string? ActivationPlainText);

    private Task ChangeAsync(UserId caller, UserId target, string email, string? reason)
        => DispatchAsync<ChangeUserEmailCommand, ChangeUserEmailResult>(caller, new ChangeUserEmailCommand(target, email, reason));

    private Task ReactivateAsync(UserId caller, UserId target)
        => DispatchAsync<ReactivateUserCommand, ReactivateUserResult>(caller, new ReactivateUserCommand(target, "Rehired."));

    private async Task<TResult> DispatchAsync<TCommand, TResult>(UserId caller, TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<TCommand, TResult>(command, CancellationToken.None);
    }

    /// <summary>CRD-C1, anonymous, as the activation page calls it.</summary>
    private async Task ActivateAsync(string plainText)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(plainText, "a-sufficiently-long-password"), CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    /// <summary>The refusal's sentence, whether the domain or the application stated it.</summary>
    private static async Task<string> RefusalAsync(Func<Task> dispatch)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(dispatch);

        Assert.True(
            refusal is BusinessRuleViolationException or DomainException,
            $"Expected a refusal, got {refusal.GetType().Name}: {refusal.Message}");

        return refusal.Message;
    }

    private async Task<UserId> AdminAsync() => await CallerAsync("usr-c3-user-administrator", "user-administrator");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    private static List<(string Name, string? Value)> Members(string json)
        => [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(x => (x.Name, x.Value.GetString()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

    /// <summary>
    /// A human with two local identities, an open reset token, an open
    /// activation token (whose plaintext is kept for a pending user), a used
    /// token, a live session, a role assignment and, unless pending, a
    /// credential.
    /// </summary>
    private async Task<Target> SeedAsync(bool inactive = false, bool pending = false, string? email = null)
    {
        var userId = Guid.NewGuid();
        var unique = userId.ToString("N");
        email ??= $"usr-c3-{unique}@example.test";

        var primary = Guid.NewGuid();
        var secondary = Guid.NewGuid();
        var resetToken = Guid.NewGuid();
        var usedToken = Guid.NewGuid();
        var activationId = UserTokenId.New();
        var activation = new UserTokenService().Generate(activationId);
        var activationToken = activationId.Value;
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'Grace', 'Mover', 'Grace Mover {unique[..8]}', @email, {status},
                     now() - interval '1 year', '{system}', now() - interval '1 year', '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, deactivated_at, deactivated_by, created_at, created_by)
             VALUES ('{primary}', '{userId}', 'Human', 'Local', 'Application', '{primary}',
                     'c3-a-{unique[..20]}', {status}, now() - interval '1 year', '{system}'),
                    ('{secondary}', '{userId}', 'Human', 'Local', 'Application', '{secondary}',
                     'c3-b-{unique[..20]}', {status}, now() - interval '1 year', '{system}');

             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason)
             SELECT '{Guid.NewGuid()}', '{userId}', 'Human', r.id, 'Global', NULL,
                    now() - interval '30 days', NULL, now() - interval '30 days', '{system}', 'Seeded for USR-C3 tests.'
               FROM role r WHERE r.code = 'access-reviewer';

             INSERT INTO user_session (id, user_identity_id, created_at, last_activity_at, expires_at,
                                       revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
             VALUES ('{Guid.NewGuid()}', '{primary}', now() - interval '5 minutes', now(), now() + interval '8 hours',
                     NULL, NULL, NULL, NULL, 'usr-c3-tests/1.0');

             INSERT INTO user_token (id, user_identity_id, token_type, token_hash, expires_at,
                                     used_at, invalidated_at, created_at, created_by)
             VALUES ('{resetToken}', '{primary}', 'PasswordReset', 'reset-{unique}', now() + interval '1 hour',
                     NULL, NULL, now(), '{system}'),
                    ('{activationToken}', '{secondary}', 'Activation', @activation, now() + interval '72 hours',
                     NULL, NULL, now(), '{system}'),
                    ('{usedToken}', '{primary}', 'Activation', 'used-{unique}', now() + interval '72 hours',
                     now() - interval '300 days', NULL, now() - interval '301 days', '{system}');
             """, connection);

        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("activation", activation.Hash);

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

        return new Target(new UserId(userId), email, resetToken, activationToken, usedToken, pending ? activation.PlainText : null);
    }

    private async Task<UserId> SeedAgentAsync()
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Agent', NULL, NULL, 'Agent {id:N}', NULL, 'Active',
                     now(), '{system}', now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return new UserId(id);
    }

    private Task<string> EmailAsync(UserId user)
        => ScalarAsync<string>("SELECT email FROM app_user WHERE id = @id", user.Value);

    private Task<long> RecordsAsync(UserId user)
        => ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_record WHERE event_type = 'UserEmailChanged' AND entity_id = @id",
            user.Value);

    private async Task<DateTime?> InvalidatedAtAsync(Guid token)
        => await ScalarAsync<object>("SELECT invalidated_at FROM user_token WHERE id = @id", token) as DateTime?;

    /// <summary>The row and everything hanging off it, tokens included.</summary>
    private Task<string> SnapshotAsync(UserId user)
        => ScalarAsync<string>(
            """
            SELECT concat_ws('|', u.email, u.updated_at, (SELECT string_agg(concat_ws(',', t.id, t.invalidated_at), ';' ORDER BY t.id)
                                                          FROM user_token t JOIN user_identity i ON i.id = t.user_identity_id
                                                         WHERE i.user_id = u.id))
              FROM app_user u WHERE u.id = @id
            """, user.Value);

    /// <summary>Everything CE-A8 says is untouched: all but the email and the tokens.</summary>
    private Task<string> EverythingElseAsync(UserId user)
        => ScalarAsync<string>(
            """
            SELECT concat_ws('|', u.first_name, u.last_name, u.display_name, u.status, u.deactivated_at,
                   (SELECT string_agg(concat_ws(',', i.id, i.status, i.username, i.deactivated_at), ';' ORDER BY i.id)
                      FROM user_identity i WHERE i.user_id = u.id),
                   (SELECT string_agg(concat_ws(',', c.id, c.password_hash, c.failed_attempt_count, c.must_change_password), ';' ORDER BY c.id)
                      FROM credential c JOIN user_identity i ON i.id = c.user_identity_id WHERE i.user_id = u.id),
                   (SELECT string_agg(concat_ws(',', s.id, s.revoked_at, s.last_activity_at), ';' ORDER BY s.id)
                      FROM user_session s JOIN user_identity i ON i.id = s.user_identity_id WHERE i.user_id = u.id),
                   (SELECT string_agg(concat_ws(',', r.id, r.effective_to, r.revoked_at), ';' ORDER BY r.id)
                      FROM user_role r WHERE r.user_id = u.id))
              FROM app_user u WHERE u.id = @id
            """, user.Value);

    private async Task<T> ScalarAsync<T>(string sql, object id)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task<object?[]> RowAsync(string sql, object id)
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

        return Assert.Single(rows);
    }
}
