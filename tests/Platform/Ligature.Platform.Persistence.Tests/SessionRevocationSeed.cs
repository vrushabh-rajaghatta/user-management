using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Shared seeding and reading for the SES-C3 and SES-C4 integration tests.
///
/// Session timestamps come from the application clock the handlers read, so
/// the idle-tolerance boundary — the one place "active" is decided by seconds —
/// is exact rather than hostage to drift between the application and the
/// database.
/// </summary>
internal sealed class SessionRevocationSeed
{
    private static readonly TimeSpan Idle = SecurityBaseline.Current.SessionIdleTimeout;
    private static readonly TimeSpan Tolerance = Services.CallerEstablisher.EnforcementTolerance;

    private readonly ActivationDatabase _database;

    internal SessionRevocationSeed(ActivationDatabase database) => _database = database;

    internal enum Kind
    {
        Active,
        IdleWithinTolerance,
        IdlePastTolerance,
        Revoked,
        Expired,
        ExpiredRecentlyActive,
    }

    internal sealed record Person(UserId UserId, Guid LocalIdentityId);

    internal sealed record SessionRow(bool IsRevoked, Guid? RevokedBy, string? Reason, string? RevokedAt);

    internal sealed record RevocationRecord(
        string OriginKind, Guid? ActorUserId, string? Reason, string? Code, string EntityType,
        Guid? TargetIdentity, Guid? SubjectUser);

    // ------------------------------------------------------------ callers

    internal Task<PermanentCaller> CallerAsync(string label, string? roleCode)
        => PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, roleCode);

    /// <summary>
    /// An Agent user holding a role that contains only session.revoke. Agent
    /// assignments must be finite (UR8), and the role carries no human-only
    /// permission (UR9), so the assignment is legal — which is the point: the
    /// seed does not make session.revoke human-only (D3).
    ///
    /// It also holds an active identity, because authorisation denies any actor
    /// with none (AUT-Q1). External, since an agent has no local password.
    /// </summary>
    internal async Task<UserId> AgentRevokerAsync()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N")[..12];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Agent', NULL, NULL, 'Revoker Agent {unique}', NULL, 'Active',
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{userId}', 'Agent', 'External', 'EntraId',
                  'agent-{unique}', NULL, 'Active', now() - interval '1 day', '{system}');

             INSERT INTO role
                 (id, name, code, description, is_system_role, is_active,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{roleId}', 'Session Revoker {unique}', 'session-revoker-{unique}', NULL,
                  false, true, now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by)
             SELECT '{Guid.NewGuid()}', '{roleId}', p.id, now() - interval '1 day', '{system}'
             FROM permission p WHERE p.code = 'session.revoke';

             INSERT INTO user_role
                 (id, user_id, actor_type, role_id, scope_type, scope_id,
                  effective_from, effective_to, assigned_at, assigned_by, assignment_reason)
             VALUES
                 ('{Guid.NewGuid()}', '{userId}', 'Agent', '{roleId}', 'Global', NULL,
                  now() - interval '1 day', now() + interval '30 days', now(), '{system}',
                  'SES-C3/C4 agent authorisation test.');
             """);

        return new UserId(userId);
    }

    /// <summary>
    /// An Agent user with an active identity and a session that is otherwise
    /// active. The per-request check refuses any non-human session (inv. 26), so
    /// the canonical test must not count it as active.
    /// </summary>
    internal async Task<(UserId UserId, Guid SessionId)> AgentWithSessionAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N")[..12];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Agent', NULL, NULL, 'Session Agent {unique}', NULL, 'Active',
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Agent', 'External', 'EntraId',
                  'session-agent-{unique}', NULL, 'Active', now() - interval '1 day', '{system}');
             """);

        return (new UserId(userId), await SessionAsync(identityId));
    }

    internal async Task DispatchAsync<TCommand, TResult>(
        UserId? caller, TCommand command, ActorType actorType = ActorType.Human)
        where TCommand : ICommand<TResult>
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

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
            .SendAsync<TCommand, TResult>(command, CancellationToken.None);
    }

    // ------------------------------------------------------------ seeding

    internal async Task<Person> PersonAsync(bool userActive = true)
    {
        var userId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Session', 'Holder', 'Session Holder',
                  'sessions-{unique}@example.test', '{(userActive ? "Active" : "Inactive")}',
                  {(userActive ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}', now(), '{system}')
             """);

        var local = await IdentityAsync(new UserId(userId), external: false);

        return new Person(new UserId(userId), local);
    }

    internal async Task<Guid> IdentityAsync(UserId userId, bool external, bool active = true)
    {
        var id = Guid.NewGuid();
        var unique = id.ToString("N");
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, deactivated_at, deactivated_by,
                  created_at, created_by)
             VALUES
                 ('{id}', '{userId.Value}', 'Human', '{(external ? "External" : "Local")}',
                  '{(external ? "EntraId" : "Application")}',
                  '{(external ? $"external-{unique}" : id.ToString())}',
                  {(external ? "NULL" : $"'holder-{unique[..20]}'")},
                  '{(active ? "Active" : "Inactive")}',
                  {(active ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}')
             """);

        return id;
    }

    internal async Task<Guid> SessionAsync(Guid identityId, Kind kind = Kind.Active)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        var (created, lastActivity, expires) = kind switch
        {
            Kind.Active or Kind.Revoked => (now.AddHours(-1), now.AddMinutes(-1), now.AddHours(8)),
            Kind.IdleWithinTolerance => (now.AddHours(-1), now - Idle - TimeSpan.FromSeconds(20), now.AddHours(8)),
            Kind.IdlePastTolerance => (now.AddHours(-1), now - Idle - Tolerance - TimeSpan.FromMinutes(2), now.AddHours(8)),
            Kind.Expired => (now.AddDays(-2), now.AddDays(-2), now.AddDays(-1)),
            Kind.ExpiredRecentlyActive => (now.AddHours(-1), now.AddSeconds(-30), now.AddSeconds(-10)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var revoked = kind == Kind.Revoked;

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at,
                 revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
            VALUES
                (@id, @identity, @created, @lastActivity, @expires,
                 @revokedAt, @revokedBy, @reason, NULL, 'session-revocation-tests/1.0')
            """, connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identity", identityId);
        command.Parameters.AddWithValue("created", created);
        command.Parameters.AddWithValue("lastActivity", lastActivity);
        command.Parameters.AddWithValue("expires", expires);
        command.Parameters.AddWithValue("revokedAt", revoked ? now.AddMinutes(-10) : DBNull.Value);
        command.Parameters.AddWithValue("revokedBy", revoked ? User.SystemUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("reason", revoked ? "Logout" : DBNull.Value);

        await command.ExecuteNonQueryAsync();

        return id;
    }

    internal Task DeactivateIdentityAsync(Guid identityId)
        => ExecuteAsync(
            $"""
             UPDATE user_identity
             SET status = 'Inactive', deactivated_at = now(), deactivated_by = '{User.SystemUserId.Value}'
             WHERE id = '{identityId}'
             """);

    // ------------------------------------------------------------ reading

    internal async Task<SessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT revoked_at IS NOT NULL, revoked_by, revocation_reason, revoked_at::text
            FROM user_session WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The session row is missing.");

        return new SessionRow(
            reader.GetBoolean(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    internal async Task<IReadOnlyList<RevocationRecord>> ReadRevocationsAsync(Guid sessionId)
    {
        var rows = new List<RevocationRecord>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.origin_kind, r.actor_user_id, r.reason,
                   r.after::jsonb ->> 'RevocationReason', r.entity_type,
                   (SELECT e.entity_id FROM audit.audit_entity_ref e
                     WHERE e.audit_id = r.audit_id AND e.entity_type = 'Identity' AND e.ref_role = 'Target'),
                   (SELECT e.entity_id FROM audit.audit_entity_ref e
                     WHERE e.audit_id = r.audit_id AND e.entity_type = 'User' AND e.ref_role = 'Subject')
            FROM audit.audit_record r
            WHERE r.event_type = 'SessionRevoked'
              AND r.entity_type = 'Session' AND r.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RevocationRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));
        }

        return rows;
    }

    /// <summary>SignedOut records naming any of these sessions — must stay zero.</summary>
    internal async Task<long> CountSignedOutAsync(IEnumerable<Guid> sessionIds)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_record WHERE event_type = 'SignedOut' AND entity_id = ANY(@ids)",
            connection);

        command.Parameters.AddWithValue("ids", sessionIds.ToArray());

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// The footprint of a set of sessions: every row's revocation columns and
    /// every SessionRevoked naming them. A no-op or a refusal leaves it equal.
    /// </summary>
    internal async Task<string> FootprintAsync(IEnumerable<Guid> sessionIds)
    {
        var parts = new List<string>();

        foreach (var id in sessionIds.OrderBy(x => x))
        {
            var row = await ReadSessionAsync(id);
            var records = await ReadRevocationsAsync(id);

            parts.Add($"{id}:{row.IsRevoked}:{row.RevokedBy}:{row.Reason}:{row.RevokedAt}:{records.Count}");
        }

        return string.Join("|", parts);
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
