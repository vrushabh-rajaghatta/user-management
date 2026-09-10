using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C1's records, and with them the two things E2b exists to make possible.
///
/// A sign-in is the only command that produces records on both write paths with
/// two different actors. The attempt is anonymous and autonomous, because it
/// records a failure and must outlive the transaction that failed. The lock is
/// the system's own act on the command's transaction. The success is the
/// caller's, and can only be recorded because the command established them.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather than
/// skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class AuditEmissionSignInTests
{
    private const string GoodPassword = "a-sufficiently-long-password";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_successful_sign_in_is_recorded_as_the_callers_own_act()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var result = await SignInAsync(provider, fixture.Username, GoodPassword);

            Assert.True(result.Succeeded);

            var record = Assert.Single(await ReadAsync("SignInSucceeded", result.SessionId!.Value));

            Assert.Equal("Transactional", record.WritePath);
            Assert.Equal("Session", record.EntityType);

            // The person who signed in, established by the command itself from
            // the identity whose password verified (AUD-D28).
            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(fixture.UserId.Value, record.ActorUserId);
            Assert.Equal("Human", record.ActorType);

            // Nobody granted a sign-in; there is no authority to name.
            Assert.Null(record.AuthorizingRoleId);
            Assert.Null(record.AuthorizingAssignmentId);

            Assert.Contains("203.0.113.7", record.Payload);

            Assert.Equal(
                [$"Identity/{fixture.IdentityId.Value}/Target",
                 $"User/{fixture.UserId.Value}/Subject"],
                await ReadReferencesAsync(record.AuditId));
        });
    }

    /// <summary>
    /// The proof E2b was built for. One command, one operation, two records:
    /// one written on the command's transaction under the System actor, one
    /// written on a connection of its own under no actor at all.
    /// </summary>
    [Fact]
    public async Task The_locking_attempt_records_the_attempt_and_the_lock_differently()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var threshold = SecurityBaseline.Current.MaxFailedLoginAttempts;
            var before = await ReadLastSequenceAsync();

            for (var attempt = 0; attempt < threshold; attempt++)
                Assert.False((await SignInAsync(provider, fixture.Username, "wrong-password")).Succeeded);

            var locked = Assert.Single(
                await ReadAsync("AccountLocked", credentialOf: fixture.IdentityId, after: before));

            var attempts = await ReadAsync(
                "SignInFailed", identityOf: fixture.IdentityId, after: before);

            Assert.Equal(threshold, attempts.Count);

            // The last attempt is the one that crossed the threshold, so it and
            // the lock belong to the same command (AR15).
            var final = attempts[^1];

            Assert.Equal(final.OperationId, locked.OperationId);

            // The attempt: nobody authenticated, and it commits on its own.
            Assert.Equal("Autonomous", final.WritePath);
            Assert.Equal("Anonymous", final.OriginKind);
            Assert.Null(final.ActorUserId);

            // The lock: the system's act, on the command's transaction.
            Assert.Equal("Transactional", locked.WritePath);
            Assert.Equal("System", locked.OriginKind);
            Assert.Equal(User.SystemUserId.Value, locked.ActorUserId);
            Assert.Equal("System", locked.ActorType);
            Assert.Equal(User.SystemDisplayName, locked.ActorDisplayName);

            Assert.Contains($"\"FailedAttemptCount\": {threshold}", locked.After);
            Assert.DoesNotContain("\"LockedUntil\": null", locked.After);
            Assert.Contains("\"LockedUntil\": null", locked.Before);
        });
    }

    /// <summary>
    /// The attempted identifier is recorded as typed, however it looks. It is
    /// declared PII by the catalogue, so the secret scan does not judge its
    /// shape — otherwise a long enough username would turn a refused sign-in
    /// into a 500, and which usernames did that would be measurable.
    /// </summary>
    [Fact]
    public async Task An_identifier_shaped_like_a_secret_is_still_recorded()
    {
        await RunAsync(async (provider, _) =>
        {
            const string secretShaped = "aGVsbG8gd29ybGQgdGhpcyBpczMyKw==";

            var before = await ReadLastSequenceAsync();

            Assert.False((await SignInAsync(provider, secretShaped, GoodPassword)).Succeeded);

            var records = await ReadAsync(
                "SignInFailed", attemptedIdentifier: secretShaped, after: before);

            var record = Assert.Single(records);

            Assert.Equal("Anonymous", record.OriginKind);
            Assert.Contains("IdentityNotUsable", record.Payload);

            // No identity was resolved, so there is none to name — which the
            // catalogue permits for this event and for no other reason.
            Assert.Null(record.EntityId);
            Assert.Equal("Identity", record.EntityType);
        });
    }

    /// <summary>
    /// The frozen failure semantics, on the path where the command SUCCEEDS: a
    /// refused sign-in is a successful command whose answer is no, and its
    /// counter commits. If the record of it cannot be written, the request
    /// fails — but the counter it already committed stays committed.
    /// </summary>
    [Fact]
    public async Task A_failed_autonomous_write_fails_the_request_and_leaves_the_commit()
    {
        await RunAsync(async (provider, fixture) =>
        {
            Assert.Equal(0, await ReadFailedAttemptCountAsync(fixture.IdentityId));

            await using var broken = BuildProvider(autonomousWriterThrows: true);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SignInAsync(broken, fixture.Username, "wrong-password"));

            Assert.Contains("the command succeeded and its change is committed", failure.Message);

            // The business change stands. It was committed before the
            // autonomous write was attempted, and failing the request does not
            // — and must not pretend to — undo it.
            Assert.Equal(1, await ReadFailedAttemptCountAsync(fixture.IdentityId));
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Fixture(UserId UserId, UserIdentityId IdentityId, string Username);

    private static async Task RunAsync(Func<IServiceProvider, Fixture, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var fixture = await SeedAsync();

        try
        {
            await using var provider = BuildProvider(autonomousWriterThrows: false);

            await body(provider, fixture);
        }
        finally
        {
            await ResetAsync(fixture.IdentityId);
        }
    }

    private static async Task<SignInResult> SignInAsync(
        IServiceProvider provider, string username, string password)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignInCommand, SignInResult>(
                new SignInCommand(username, password, "203.0.113.7", "audit-tests/1.0"),
                CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(bool autonomousWriterThrows)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(TestDatabase.ConnectionString);

        services.AddSingleton<IClock>(new FixedClock(Now));

        if (autonomousWriterThrows)
        {
            services.AddSingleton<IAutonomousAuditRecordWriter>(
                new ThrowingAutonomousWriter());
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// A permanent person, because a successful sign-in makes them an actor,
    /// with a credential of this run's own so the counters start at zero.
    /// </summary>
    private static async Task<Fixture> SeedAsync()
    {
        const string label = "audit-signin";

        var caller = await PermanentTestCaller.EnsureAsync(
            TestDatabase.ConnectionString, label, roleCode: null);

        await ResetAsync(caller.IdentityId);

        var hashed = new PasswordHasher().Hash(GoodPassword);

        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(new FixedClock(Now), executionContext: null))
            .Options;

        await using var context = new LigatureDbContext(options);

        context.Add(Credential.Create(
            CredentialId.New(), caller.IdentityId, IdentityType.Local,
            hashed.Hash, hashed.Algorithm,
            passwordChangedAt: Now.AddDays(-30), mustChangePassword: false,
            createdAt: Now, createdBy: User.SystemUserId));

        await context.SaveChangesAsync(CancellationToken.None);

        return new Fixture(caller.UserId, caller.IdentityId, $"permanent-{label}");
    }

    private static async Task ResetAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM password_history WHERE user_identity_id = @id",
            "DELETE FROM user_session WHERE user_identity_id = @id",
            "DELETE FROM credential WHERE user_identity_id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", identityId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// The trail's high-water mark. These records are permanent — a failed
    /// sign-in is autonomous and commits on its own — and the person who made
    /// them is permanent too, so every run adds to what came before. Reads are
    /// therefore scoped to what THIS test caused.
    /// </summary>
    private static async Task<long> ReadLastSequenceAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(sequence), 0) FROM audit.audit_record", connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ReadFailedAttemptCountAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(failed_attempt_count), 0) FROM credential WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // ------------------------------------------------------------- readers

    private sealed record Record(
        Guid AuditId, string EventType, string WritePath, string OriginKind,
        Guid? ActorUserId, string? ActorType, string? ActorDisplayName,
        Guid? AuthorizingRoleId, Guid? AuthorizingAssignmentId,
        string EntityType, Guid? EntityId, Guid OperationId,
        string? Before, string? After, string? Payload);

    /// <summary>
    /// Scoped to this fixture rather than counting the trail: other classes run
    /// in parallel against this database and sign in as people of their own.
    /// </summary>
    private static async Task<IReadOnlyList<Record>> ReadAsync(
        string eventType,
        Guid? entityId = null,
        UserIdentityId? identityOf = null,
        UserIdentityId? credentialOf = null,
        string? attemptedIdentifier = null,
        long after = 0)
    {
        var where = (entityId, identityOf, credentialOf, attemptedIdentifier) switch
        {
            (not null, _, _, _) => "entity_id = @entityId",
            (_, not null, _, _) => "entity_id = @identityId",
            (_, _, not null, _) =>
                "entity_id IN (SELECT id FROM credential WHERE user_identity_id = @identityId)",
            _ => "payload ->> 'AttemptedIdentifier' = @identifier",
        };

        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT audit_id, event_type, write_path, origin_kind, actor_user_id,
                   actor_type, actor_display_name, authorizing_role_id,
                   authorizing_assignment_id, entity_type, entity_id,
                   operation_id, before::text, after::text, payload::text
            FROM audit.audit_record
            WHERE event_type = @eventType AND sequence > @after AND {where}
            ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("after", after);

        if (entityId is not null)
            command.Parameters.AddWithValue("entityId", entityId.Value);

        if (identityOf is not null || credentialOf is not null)
            command.Parameters.AddWithValue("identityId", (identityOf ?? credentialOf)!.Value);

        if (attemptedIdentifier is not null)
            command.Parameters.AddWithValue("identifier", attemptedIdentifier);

        var records = new List<Record>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new Record(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return records;
    }

    private static async Task<IReadOnlyList<string>> ReadReferencesAsync(Guid auditId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type || '/' || entity_id || '/' || ref_role
            FROM audit.audit_entity_ref WHERE audit_id = @id
            ORDER BY entity_type, ref_role
            """, connection);

        command.Parameters.AddWithValue("id", auditId);

        var refs = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            refs.Add(reader.GetString(0));

        return refs;
    }

    private sealed class ThrowingAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The autonomous writer is broken.");
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;

        internal FixedClock(DateTimeOffset now) => _now = now;

        public DateTimeOffset UtcNow => _now;
    }
}
