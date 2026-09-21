using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Queries.UserSessions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// SES-Q1 GetActiveSessions for one user (docs/requirements.md, "SES-Q1
/// GetActiveSessions and Revoke on the User detail page", SQ-1 to SQ-5),
/// against PostgreSQL.
///
/// THE BASELINE IS THE EXISTING SESSION SEMANTICS. "Active" is the canonical
/// test the per-request check and SES-C3 already apply; SQ-2 holds the read
/// and the repository to the same verdict session by session, so the list can
/// never show a session Revoke would treat as over, or hide one it would end.
///
/// Its own database: sessions are seeded directly with the timestamps each
/// case needs, against the real clock.
/// </summary>
public sealed class UserSessionsIntegrationTests : IClassFixture<ActivationDatabase>
{
    /// <summary>The per-request check's enforcement tolerance (docs/architecture.md §17).</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(60);

    private readonly ActivationDatabase _database;

    public UserSessionsIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ---------------------------------------------------------------- SQ-1

    [Fact]
    public async Task Exactly_the_active_sessions_of_every_identity_are_read_most_recent_first()
    {
        var reader = await CallerAsync("access-reviewer");
        var idle = await IdleTimeoutAsync();
        var user = await SeedUserAsync();
        var first = await AddIdentityAsync(user);
        var second = await AddIdentityAsync(user);

        var older = await AddSessionAsync(first, lastActiveAgo: TimeSpan.FromMinutes(10));
        var newest = await AddSessionAsync(second, lastActiveAgo: TimeSpan.FromMinutes(1));
        var middle = await AddSessionAsync(first, lastActiveAgo: TimeSpan.FromMinutes(5), ip: null, userAgent: null);

        await AddSessionAsync(first, lastActiveAgo: TimeSpan.FromMinutes(2), revoked: true);
        await AddSessionAsync(first, lastActiveAgo: TimeSpan.FromMinutes(2), expired: true);
        await AddSessionAsync(second, lastActiveAgo: idle + Tolerance + TimeSpan.FromMinutes(5));

        var result = await ReadAsync(reader, user, callerSession: null);

        Assert.Equal([newest, middle, older], result.Sessions.Select(x => x.SessionId.Value));

        var row = result.Sessions.Single(x => x.SessionId.Value == newest);
        Assert.Equal("203.0.113.9", row.IpAddress);
        Assert.Equal("integration-tests/1.0", row.UserAgent);
        Assert.False(row.Current);
        Assert.True(row.ExpiresAt > row.CreatedAt);

        var bare = result.Sessions.Single(x => x.SessionId.Value == middle);
        Assert.Null(bare.IpAddress);
        Assert.Null(bare.UserAgent);
    }

    [Fact]
    public async Task Sessions_active_at_the_same_instant_are_ordered_by_id()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();
        var identity = await AddIdentityAsync(user);

        var instant = "date_trunc('second', now()) - interval '3 minutes'";
        var ids = new[]
        {
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.Zero, lastActiveSql: instant),
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.Zero, lastActiveSql: instant),
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.Zero, lastActiveSql: instant),
        };

        var result = await ReadAsync(reader, user, callerSession: null);

        Assert.Equal(ids.Order(), result.Sessions.Select(x => x.SessionId.Value));
    }

    // ---------------------------------------------------------------- SQ-2

    /// <summary>
    /// The read and SES-C3 agree, session by session: listed exactly when the
    /// repository's canonical test accepts it. Including a session inside the
    /// enforcement tolerance, which is still active though past its idle instant.
    /// </summary>
    [Fact]
    public async Task The_read_lists_exactly_what_the_canonical_test_accepts()
    {
        var reader = await CallerAsync("access-reviewer");
        var idle = await IdleTimeoutAsync();
        var user = await SeedUserAsync();
        var identity = await AddIdentityAsync(user);

        var seeded = new[]
        {
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1)),
            await AddSessionAsync(identity, lastActiveAgo: idle + TimeSpan.FromSeconds(20)),
            await AddSessionAsync(identity, lastActiveAgo: idle + Tolerance + TimeSpan.FromMinutes(1)),
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1), revoked: true),
            await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1), expired: true),
        };

        var listed = (await ReadAsync(reader, user, callerSession: null))
            .Sessions.Select(x => x.SessionId.Value).ToHashSet();

        await using var provider = Provider();

        foreach (var id in seeded)
        {
            using var scope = provider.CreateScope();

            var accepted = await scope.ServiceProvider
                .GetRequiredService<IUserSessionRepository>()
                .FindActiveAsync(new UserSessionId(id), DateTimeOffset.UtcNow, idle, CancellationToken.None);

            Assert.Equal(accepted is not null, listed.Contains(id));
        }

        // The tolerance case is listed, with its conservative idle instant already past.
        Assert.Contains(seeded[1], listed);
        Assert.Equal(2, listed.Count);
    }

    // ---------------------------------------------------------------- SQ-3

    [Fact]
    public async Task Idle_expires_at_is_last_activity_plus_the_effective_idle_timeout_without_tolerance()
    {
        var reader = await CallerAsync("access-reviewer");
        var idle = await IdleTimeoutAsync();
        var user = await SeedUserAsync();
        var identity = await AddIdentityAsync(user);
        await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(4));

        var row = Assert.Single((await ReadAsync(reader, user, callerSession: null)).Sessions);

        Assert.Equal(row.LastActivityAt + idle, row.IdleExpiresAt);
    }

    // ---------------------------------------------------------------- SQ-4

    [Fact]
    public async Task An_inactive_user_has_no_active_sessions()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync(inactive: true);
        var identity = await AddIdentityAsync(user);
        await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1));

        Assert.Empty((await ReadAsync(reader, user, callerSession: null)).Sessions);
    }

    [Fact]
    public async Task An_unknown_user_and_the_system_actor_are_refused_as_unknown()
    {
        var reader = await CallerAsync("access-reviewer");

        await AssertRefusedAsync(
            () => ReadAsync(reader, new UserId(Guid.NewGuid()), callerSession: null),
            "The user does not exist.");

        await AssertRefusedAsync(
            () => ReadAsync(reader, User.SystemUserId, callerSession: null),
            "The user does not exist.");
    }

    [Fact]
    public async Task Without_session_read_the_read_is_refused()
    {
        var security = await CallerAsync("security-administrator");
        var user = await SeedUserAsync();

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => ReadAsync(security, user, callerSession: null));

        Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_a_caller_the_read_is_refused()
    {
        var user = await SeedUserAsync();

        await using var provider = Provider();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>()
                .SendAsync<UserSessionsQuery, UserSessionsResult>(
                    new UserSessionsQuery(user, null), CancellationToken.None));
    }

    [Fact]
    public async Task The_read_is_not_audited()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();
        var identity = await AddIdentityAsync(user);
        await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1));

        var before = await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record");

        await ReadAsync(reader, user, callerSession: null);

        Assert.Equal(before, await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record"));
    }

    // ---------------------------------------------------------------- SQ-5

    /// <summary>
    /// Session-level, not identity-level: two sessions of the caller's own
    /// identity, and only the one the caller is using is current.
    /// </summary>
    [Fact]
    public async Task Only_the_callers_own_session_is_current()
    {
        var admin = await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "ses-q1-self", "user-administrator");

        var one = await AddSessionAsync(admin.IdentityId.Value, lastActiveAgo: TimeSpan.FromMinutes(1));
        var two = await AddSessionAsync(admin.IdentityId.Value, lastActiveAgo: TimeSpan.FromMinutes(2));

        var asOne = await ReadAsync(admin.UserId, admin.UserId, callerSession: one);
        Assert.Equal([one], asOne.Sessions.Where(x => x.Current).Select(x => x.SessionId.Value));
        Assert.Contains(asOne.Sessions, x => x.SessionId.Value == two && !x.Current);

        var asTwo = await ReadAsync(admin.UserId, admin.UserId, callerSession: two);
        Assert.Equal([two], asTwo.Sessions.Where(x => x.Current).Select(x => x.SessionId.Value));
    }

    [Fact]
    public async Task Another_users_sessions_are_never_current()
    {
        var admin = await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "ses-q1-other", "user-administrator");

        var own = await AddSessionAsync(admin.IdentityId.Value, lastActiveAgo: TimeSpan.FromMinutes(1));

        var user = await SeedUserAsync();
        var identity = await AddIdentityAsync(user);
        await AddSessionAsync(identity, lastActiveAgo: TimeSpan.FromMinutes(1));

        var result = await ReadAsync(admin.UserId, user, callerSession: own);

        Assert.NotEmpty(result.Sessions);
        Assert.DoesNotContain(result.Sessions, x => x.Current);
    }

    // ================================================================ harness

    private async Task<UserId> CallerAsync(string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"ses-q1-{role}", role)).UserId;

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task<UserSessionsResult> ReadAsync(UserId caller, UserId target, Guid? callerSession)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserSessionsQuery, UserSessionsResult>(
                new UserSessionsQuery(target, callerSession is { } id ? new UserSessionId(id) : null),
                CancellationToken.None);
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

    private static async Task AssertRefusedAsync(Func<Task> dispatch, string message)
    {
        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);
        Assert.Equal(message, refusal.Message);
    }

    private async Task<UserId> SeedUserAsync(bool inactive = false)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var status = inactive ? "'Inactive', now() - interval '1 day', '" + system + "'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Many', 'Sessions', 'Many Sessions', 'sessions-{id:N}@example.test',
                     {status}, now() - interval '1 year', '{system}', now(), '{system}')
             """);

        return new UserId(id);
    }

    private async Task<Guid> AddIdentityAsync(UserId user)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{id}', '{user.Value}', 'Human', 'Local', 'Application', '{id}', 'ses-{id:N}', 'Active',
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
        string? userAgent = "integration-tests/1.0",
        string? lastActiveSql = null)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var lastActive = lastActiveSql ?? $"now() - interval '{(long)lastActiveAgo.TotalSeconds} seconds'";

        // Created well before the last activity; an expired session's absolute
        // lifetime ended a minute ago, anything else's is hours away.
        var expires = expired ? "now() - interval '1 minute'" : "now() + interval '6 hours'";
        var revocation = revoked
            ? $"now() - interval '30 seconds', '{system}', 'AdminRevoked'"
            : "NULL, NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO user_session (id, user_identity_id, created_at, last_activity_at, expires_at,
                                       revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
             VALUES ('{id}', '{identityId}', now() - interval '2 days', {lastActive}, {expires},
                     {revocation},
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
