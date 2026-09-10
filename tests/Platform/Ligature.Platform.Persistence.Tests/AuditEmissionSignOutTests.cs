using System.Globalization;
using System.Text.Json;
using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.SignOut;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C2's record, and the four outcomes that must not produce one.
///
/// Sign-out is the command where "declare nothing" carries as much weight as
/// "declare this". Every outcome looks identical to the caller — an unknown
/// session, someone else's, an already-revoked one and a successful revocation
/// all answer the same — and three of those four changed no state. A record
/// for any of them would assert a revocation that did not happen, and the
/// trail would have to be explained away rather than read.
///
/// The refusal of an attempt against someone else's session is a real event,
/// and it has a catalogue entry: AuthorisationDenied, which is autonomous and
/// arrives with the autonomous writer. Manufacturing a transactional stand-in
/// for it here would put the wrong record in the trail permanently.
/// </summary>
public sealed class AuditEmissionSignOutTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Signing_out_records_the_revocation_as_the_change_it_made()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            var record = Assert.Single(await ReadForSessionAsync(owner.SessionId));

            Assert.Equal("SignedOut", record.EventType);
            Assert.Equal("Transactional", record.WritePath);
            Assert.Equal("SecurityEvent", record.Classification);
            Assert.Equal("Session", record.EntityType);
            Assert.Equal(owner.SessionId.Value, record.EntityId);

            // The caller signed themselves out, so the actor and the subject of
            // the revocation are the same person (AR10, D6).
            Assert.Equal(owner.UserId.Value, record.ActorUserId);
            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(TestActorIdentity.Captured, record.ActorCapturedAt);

            // "Self" is a relationship, not a granted permission, so nothing
            // authorised this and the AR12 columns stay empty.
            Assert.Null(record.AuthorizingRoleId);
            Assert.Null(record.AuthorizingAssignmentId);

            // The catalogue requires no reason for SignedOut. Logout is the
            // session's revocation reason, which is content, not AR9's column.
            Assert.False(record.ReasonRequired);
            Assert.Null(record.Reason);

            // Compared field by field rather than as text: the column is
            // jsonb, which stores a normalised form of its own and hands back
            // its own key order and spacing, not the canonical bytes that were
            // written.
            var before = Fields(record.Before);
            var after = Fields(record.After);

            Assert.Equal(["RevocationReason", "RevokedAt", "RevokedBy"], before.Keys.Order());
            Assert.All(before.Values, x => Assert.Null(x));

            Assert.Equal("Logout", after["RevocationReason"]);
            Assert.Equal(owner.UserId.Value.ToString(), after["RevokedBy"]);
            Assert.Equal(Now, DateTimeOffset.Parse(after["RevokedAt"]!, CultureInfo.InvariantCulture));

            Assert.Null(record.Payload);
            Assert.Null(record.CausationId);

            // No refs: the catalogue declares none, and the session already
            // names the identity it belongs to.
            Assert.Empty(await ReadReferencesAsync(record.AuditId));
        });
    }

    [Fact]
    public async Task An_attempt_against_someone_elses_session_records_nothing()
    {
        await RunAsync(async (provider, owner, stranger) =>
        {
            await SignOutAsync(provider, owner.UserId, stranger.SessionId);

            Assert.Empty(await ReadForSessionAsync(stranger.SessionId));
        });
    }

    [Fact]
    public async Task An_unknown_session_records_nothing()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            var unknown = UserSessionId.New();

            await SignOutAsync(provider, owner.UserId, unknown);

            Assert.Empty(await ReadForSessionAsync(unknown));
        });
    }

    /// <summary>
    /// The second sign-out changes nothing — Revoke returns false and the
    /// first revocation's actor, reason and instant survive — so there is
    /// nothing to record. One session, one revocation, one record.
    /// </summary>
    [Fact]
    public async Task A_second_sign_out_records_nothing_further()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            await SignOutAsync(provider, owner.UserId, owner.SessionId);
            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            Assert.Single(await ReadForSessionAsync(owner.SessionId));
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Actor(UserId UserId, UserSessionId SessionId);

    private static async Task RunAsync(
        Func<IServiceProvider, Actor, Actor, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var owner = await SeedActorAsync("audit-signout-owner");
        var stranger = await SeedActorAsync("audit-signout-stranger");

        try
        {
            var services = new ServiceCollection()
                .AddPlatformApplication()
                .AddPlatformPersistence(TestDatabase.ConnectionString);

            services.AddSingleton<IClock>(new FixedClock(Now));

            await using var provider = services.BuildServiceProvider(validateScopes: true);

            await body(provider, owner, stranger);
        }
        finally
        {
            // The sessions go. The callers cannot: they are actors in the
            // trail now, and that is the point of the trail.
            await DeleteSessionAsync(owner.SessionId);
            await DeleteSessionAsync(stranger.SessionId);
        }
    }

    private static async Task SignOutAsync(
        IServiceProvider provider, UserId caller, UserSessionId sessionId)
    {
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignOutCommand, SignOutResult>(
                new SignOutCommand(sessionId), CancellationToken.None);
    }

    private static async Task<Actor> SeedActorAsync(string label)
    {
        var caller = await PermanentTestCaller.EnsureAsync(
            TestDatabase.ConnectionString, label, roleCode: null);

        var session = UserSession.Create(
            UserSessionId.New(), caller.IdentityId, Now,
            Now + SecurityBaseline.Current.SessionAbsoluteTimeout,
            ipAddress: null, userAgent: "audit-signout-tests/1.0");

        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at,
                 ip_address, user_agent)
            VALUES (@id, @identity, @now, @now, @expires, NULL, @agent)
            """, connection);

        command.Parameters.AddWithValue("id", session.Id.Value);
        command.Parameters.AddWithValue("identity", caller.IdentityId.Value);
        command.Parameters.AddWithValue("now", Now);
        command.Parameters.AddWithValue("expires", Now + SecurityBaseline.Current.SessionAbsoluteTimeout);
        command.Parameters.AddWithValue("agent", "audit-signout-tests/1.0");

        await command.ExecuteNonQueryAsync();

        return new Actor(caller.UserId, session.Id);
    }

    private static async Task DeleteSessionAsync(UserSessionId sessionId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM user_session WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", sessionId.Value);

        await command.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------- readers

    private sealed record Record(
        Guid AuditId, string EventType, string WritePath, string Classification,
        bool ReasonRequired, string? Reason, Guid? ActorUserId, string OriginKind,
        DateTimeOffset? ActorCapturedAt, Guid? AuthorizingRoleId,
        Guid? AuthorizingAssignmentId, string EntityType, Guid? EntityId,
        Guid? CausationId, string? Before, string? After, string? Payload);

    /// <summary>
    /// Scoped to one session rather than counting the whole trail: other test
    /// classes run in parallel against this database and write records of their
    /// own, so a global count would be measuring them too.
    /// </summary>
    private static async Task<IReadOnlyList<Record>> ReadForSessionAsync(UserSessionId sessionId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT audit_id, event_type, write_path, regulatory_classification,
                   reason_required, reason, actor_user_id, origin_kind,
                   actor_captured_at, authorizing_role_id,
                   authorizing_assignment_id, entity_type, entity_id,
                   causation_id, before::text, after::text, payload::text
            FROM audit.audit_record
            WHERE entity_type = 'Session' AND entity_id = @sessionId
            ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("sessionId", sessionId.Value);

        var records = new List<Record>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new Record(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16)));
        }

        return records;
    }

    private static async Task<IReadOnlyList<string>> ReadReferencesAsync(Guid auditId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT entity_type || '/' || ref_role FROM audit.audit_entity_ref WHERE audit_id = @id",
            connection);

        command.Parameters.AddWithValue("id", auditId);

        var roles = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            roles.Add(reader.GetString(0));

        return roles;
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;

        internal FixedClock(DateTimeOffset now) => _now = now;

        public DateTimeOffset UtcNow => _now;
    }

    private static Dictionary<string, string?> Fields(string? json)
    {
        Assert.NotNull(json);

        using var document = JsonDocument.Parse(json!);

        return document.RootElement.EnumerateObject().ToDictionary(
            x => x.Name,
            x => x.Value.ValueKind == JsonValueKind.Null ? null : x.Value.GetString(),
            StringComparer.Ordinal);
    }
}
