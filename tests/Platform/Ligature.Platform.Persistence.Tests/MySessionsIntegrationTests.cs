using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Queries.MySessions;
using Ligature.Platform.Application.Users.Queries.UserSessions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-Q2 GetMySessions (docs/requirements.md, "SES-Q2 GetMySessions on the
/// My account page", MS-1 to MS-4), against PostgreSQL.
///
/// SES-Q1 AND SES-Q2 SHARE SESSION SEMANTICS. MS-2 holds the two reads to
/// identical answers for the same user and caller session, so a second
/// implementation cannot drift from the first; the rest proves only what
/// differs — whose sessions, and that no permission is needed.
///
/// Its own database, with sessions seeded directly against the real clock.
/// </summary>
public sealed class MySessionsIntegrationTests : IClassFixture<ActivationDatabase>
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(60);

    private readonly ActivationDatabase _database;

    public MySessionsIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ---------------------------------------------------------------- MS-1

    [Fact]
    public async Task Exactly_the_callers_own_active_sessions_are_read_most_recent_first()
    {
        var me = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-own", roleCode: null);
        var idle = await IdleTimeoutAsync();
        var second = await AddIdentityAsync(me.UserId);

        var older = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(9));
        var newest = await AddSessionAsync(second, TimeSpan.FromMinutes(1));
        var middle = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(4));

        await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(2), revoked: true);
        await AddSessionAsync(second, TimeSpan.FromMinutes(2), expired: true);
        await AddSessionAsync(second, idle + Tolerance + TimeSpan.FromMinutes(3));

        // Someone else's active session, never listed.
        var other = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-someone-else", roleCode: null);
        var theirs = await AddSessionAsync(other.IdentityId.Value, TimeSpan.FromSeconds(10));

        var result = await ReadMineAsync(me.UserId, callerSession: newest);

        Assert.Equal([newest, middle, older], result.Sessions.Select(x => x.SessionId.Value));
        Assert.DoesNotContain(result.Sessions, x => x.SessionId.Value == theirs);
    }

    // ---------------------------------------------------------------- MS-2

    /// <summary>
    /// One semantics: the self read and the administrator's read of the same
    /// user, for the same caller session, are identical field for field.
    /// </summary>
    [Fact]
    public async Task The_self_read_and_the_administrators_read_answer_identically()
    {
        var me = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-same", roleCode: null);
        var admin = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-admin", "user-administrator");
        var idle = await IdleTimeoutAsync();

        var current = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(1));
        await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(6), ip: null, userAgent: null);
        await AddSessionAsync(me.IdentityId.Value, idle + TimeSpan.FromSeconds(20));
        await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(2), revoked: true);

        var mine = await ReadMineAsync(me.UserId, callerSession: current);
        var theirs = await ReadAsAdministratorAsync(admin.UserId, me.UserId, callerSession: current);

        Assert.NotEmpty(mine.Sessions);
        Assert.Equal(theirs.Sessions, mine.Sessions);
    }

    // ---------------------------------------------------------------- MS-3

    [Fact]
    public async Task Exactly_the_session_being_used_is_current()
    {
        var me = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-current", roleCode: null);

        var one = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(1));
        var two = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(2));

        var asOne = await ReadMineAsync(me.UserId, callerSession: one);
        Assert.Equal([one], asOne.Sessions.Where(x => x.Current).Select(x => x.SessionId.Value));

        var asTwo = await ReadMineAsync(me.UserId, callerSession: two);
        Assert.Equal([two], asTwo.Sessions.Where(x => x.Current).Select(x => x.SessionId.Value));
    }

    // ---------------------------------------------------------------- MS-4

    [Fact]
    public async Task No_permission_is_needed_to_read_ones_own_sessions()
    {
        var me = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-no-role", roleCode: null);
        var session = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(1));

        var result = await ReadMineAsync(me.UserId, callerSession: session);

        Assert.Equal([session], result.Sessions.Select(x => x.SessionId.Value));
    }

    [Fact]
    public async Task Without_a_caller_the_read_is_refused()
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>()
                .SendAsync<MySessionsQuery, MySessionsResult>(new MySessionsQuery(null), CancellationToken.None));
    }

    [Fact]
    public async Task The_read_is_not_audited()
    {
        var me = await PermanentTestCaller.EnsureAsync(_database.ConnectionString, "ses-q2-audit", roleCode: null);
        var session = await AddSessionAsync(me.IdentityId.Value, TimeSpan.FromMinutes(1));

        var before = await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record");

        await ReadMineAsync(me.UserId, callerSession: session);

        Assert.Equal(before, await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record"));
    }

    // ================================================================ harness

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task<MySessionsResult> ReadMineAsync(UserId caller, Guid callerSession)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<MySessionsQuery, MySessionsResult>(
                new MySessionsQuery(new UserSessionId(callerSession)), CancellationToken.None);
    }

    private async Task<UserSessionsResult> ReadAsAdministratorAsync(UserId caller, UserId target, Guid callerSession)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserSessionsQuery, UserSessionsResult>(
                new UserSessionsQuery(target, new UserSessionId(callerSession)), CancellationToken.None);
    }

    private async Task<TimeSpan> IdleTimeoutAsync()
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        var settings = await scope.ServiceProvider
            .GetRequiredService<ISecurityPolicyResolver>()
            .GetEffectiveSettingsAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        return settings.SessionIdleTimeout;
    }

    private async Task<Guid> AddIdentityAsync(UserId user)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{id}', '{user.Value}', 'Human', 'Local', 'Application', '{id}', 'ses2-{id:N}', 'Active',
                     now() - interval '30 days', '{system}')
             """);

        return id;
    }

    private async Task<Guid> AddSessionAsync(
        Guid identityId,
        TimeSpan lastActiveAgo,
        bool revoked = false,
        bool expired = false,
        string? ip = "203.0.113.9",
        string? userAgent = "integration-tests/1.0")
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var expires = expired ? "now() - interval '1 minute'" : "now() + interval '6 hours'";
        var revocation = revoked
            ? $"now() - interval '30 seconds', '{system}', 'AdminRevoked'"
            : "NULL, NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO user_session (id, user_identity_id, created_at, last_activity_at, expires_at,
                                       revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
             VALUES ('{id}', '{identityId}', now() - interval '2 days',
                     now() - interval '{(long)lastActiveAgo.TotalSeconds} seconds', {expires}, {revocation},
                     {(ip is null ? "NULL" : $"'{ip}'::inet")}, {(userAgent is null ? "NULL" : $"'{userAgent}'")})
             """);

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
