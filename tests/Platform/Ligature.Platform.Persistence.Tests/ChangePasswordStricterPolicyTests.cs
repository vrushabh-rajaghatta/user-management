using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C4's attempt limit, AC-4 (docs/requirements.md, "CRD-C4 — limiting
/// current-password attempts per session", L2): N is the EFFECTIVE
/// MaxFailedLoginAttempts, so a tenant that lowers it to 3 ends a session on
/// the 3rd wrong current password, not the baseline's 5th.
///
/// Its own database: a policy version applies to the whole tenant, and the
/// stricter one written here must not change what any other test sees.
/// </summary>
public sealed class ChangePasswordStricterPolicyTests
    : IClassFixture<ActivationDatabase>
{
    private const string Current = "the-current-password-1";
    private const string Wrong = "not-the-password-9";
    private const string Fresh = "an-entirely-new-password-2";
    private const int Stricter = 3;

    private readonly ActivationDatabase _database;

    public ChangePasswordStricterPolicyTests(ActivationDatabase database)
        => _database = database;

    [Fact]
    public async Task A_tenant_that_lowers_MaxFailedLoginAttempts_lowers_the_session_limit_too()
    {
        Assert.True(Stricter < SecurityBaseline.Current.MaxFailedLoginAttempts);

        await AddStricterPolicyAsync();

        var (userId, identityId) = await SeedAsync();
        var session = await InsertActiveSessionAsync(identityId);

        for (var attempt = 1; attempt < Stricter; attempt++)
        {
            var result = await DispatchAsync(userId, session);

            Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
        }

        var last = await DispatchAsync(userId, session);

        Assert.Equal(ChangePasswordOutcome.SessionEnded, last.Outcome);
        Assert.Equal(
            "PasswordChangeAttemptsExceeded",
            await ScalarAsync<string>($"SELECT revocation_reason FROM user_session WHERE id = '{session}'"));
        Assert.Equal(
            Stricter,
            await ScalarAsync<int>($"SELECT failed_password_change_attempts FROM user_session WHERE id = '{session}'"));
    }

    // ------------------------------------------------------------ harness

    /// <summary>
    /// A second policy version, effective from a minute ago: the baseline's
    /// values except MaxFailedLoginAttempts, which a tenant may lower (a CAP).
    /// </summary>
    private Task AddStricterPolicyAsync()
    {
        var baseline = SecurityBaseline.Current;

        return ExecuteAsync(
            $"""
             INSERT INTO security_policy
                 (id, policy_version, effective_from,
                  password_min_length, password_history_depth, max_failed_login_attempts,
                  lockout_duration, activation_token_lifetime, password_reset_token_lifetime,
                  session_idle_timeout, session_absolute_timeout, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}',
                  (SELECT max(policy_version) + 1 FROM security_policy),
                  now() - interval '1 minute',
                  {baseline.PasswordMinLength}, {baseline.PasswordHistoryDepth}, {Stricter},
                  interval '{(int)baseline.LockoutDuration.TotalSeconds} seconds',
                  interval '{(int)baseline.ActivationTokenLifetime.TotalSeconds} seconds',
                  interval '{(int)baseline.PasswordResetTokenLifetime.TotalSeconds} seconds',
                  interval '{(int)baseline.SessionIdleTimeout.TotalSeconds} seconds',
                  interval '{(int)baseline.SessionAbsoluteTimeout.TotalSeconds} seconds',
                  now(), '{User.SystemUserId.Value}')
             """);
    }

    private async Task<ChangePasswordResult> DispatchAsync(UserId caller, Guid sessionId)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ChangePasswordCommand, ChangePasswordResult>(
                new ChangePasswordCommand(new UserSessionId(sessionId), Wrong, Fresh),
                CancellationToken.None);
    }

    private async Task<(UserId UserId, Guid IdentityId)> SeedAsync()
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var current = new PasswordHasher().Hash(Current);

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Stricter', 'Policy', 'Stricter Policy',
                  'stricter-{unique}@example.test', 'Active',
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'stricter-{unique}', 'Active',
                  now() - interval '1 day', '{system}');

             INSERT INTO credential
                 (id, user_identity_id, identity_type, password_hash,
                  password_algorithm, password_changed_at, must_change_password,
                  failed_attempt_count, locked_until, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', 'Local', '{current.Hash}',
                  '{current.Algorithm}', now() - interval '1 day', false,
                  0, NULL, now() - interval '1 day', '{system}');
             """);

        return (new UserId(userId), identityId);
    }

    private async Task<Guid> InsertActiveSessionAsync(Guid identityId)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at, user_agent)
            VALUES
                (@id, @identity, @created, @lastActivity, @expires, 'stricter-policy-tests/1.0')
            """, connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identity", identityId);
        command.Parameters.AddWithValue("created", now.AddHours(-1));
        command.Parameters.AddWithValue("lastActivity", now.AddMinutes(-1));
        command.Parameters.AddWithValue("expires", now.AddHours(8));

        await command.ExecuteNonQueryAsync();

        return id;
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
