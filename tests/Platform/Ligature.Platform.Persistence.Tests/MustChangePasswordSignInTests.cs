using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
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
/// SES-C1 with MustChangePassword outstanding (docs/requirements.md, "SES-C1 —
/// enforcing MustChangePassword at sign-in", MC1–MC4).
///
/// After an administrator reset, the old password must not establish a
/// session: only a reset link replaces it. And a refused correct password is
/// neither a failed attempt nor a successful one — it changes nothing on the
/// credential. These tests hold both halves, because the easy mistakes are
/// exactly the two halves: counting it (so the right password locks the
/// account) or letting the success path's side effects run first (clearing
/// counters, rehashing) before refusing.
///
/// Real adapters and PostgreSQL, one DI scope per attempt, state read back
/// through fresh connections. Audit reads are scoped to sequences this test
/// caused, because other classes write to the same trail.
/// </summary>
public sealed class MustChangePasswordSignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private const string GoodPassword = "a-sufficiently-long-password";

    private const string Label = "mustchange-signin";

    private const string Username = $"permanent-{Label}";

    // ----------------------------------------------------------- the refusal

    /// <summary>MP-1, MP-2.</summary>
    [Fact]
    public async Task A_correct_password_is_refused_while_a_change_is_outstanding()
    {
        await RunAsync(async (provider, identityId) =>
        {
            var before = await LastSequenceAsync();

            var result = await SignInAsync(provider, GoodPassword);

            Assert.False(result.Succeeded);
            Assert.Null(result.SessionId);
            Assert.Equal(0, await CountSessionsAsync(identityId));
            Assert.Empty(await RecordsAsync("SignInSucceeded", identityId, before));

            var failure = Assert.Single(await RecordsAsync("SignInFailed", identityId, before));

            Assert.Equal("Autonomous", failure.WritePath);
            Assert.Equal("Anonymous", failure.OriginKind);
            Assert.Null(failure.ActorUserId);
            Assert.Equal("Identity", failure.EntityType);
            Assert.Contains("\"FailureCategory\": \"PasswordChangeRequired\"", failure.Payload, StringComparison.Ordinal);
        });
    }

    /// <summary>MP-3: not a failed attempt, and not a successful one either.</summary>
    [Fact]
    public async Task A_refusal_leaves_the_credential_exactly_as_it_was()
    {
        await RunAsync(async (provider, identityId) =>
        {
            await ExecuteAsync(
                "UPDATE credential SET failed_attempt_count = 2 WHERE user_identity_id = @id", identityId);

            var before = await ReadCredentialAsync(identityId);

            Assert.False((await SignInAsync(provider, GoodPassword)).Succeeded);

            var after = await ReadCredentialAsync(identityId);

            Assert.Equal(before, after);
            Assert.Equal(2, after.FailedAttemptCount);
            Assert.Null(after.LockedUntil);
            Assert.True(after.MustChangePassword);
        });
    }

    /// <summary>MP-3: the success path's rehash must not run before the refusal.</summary>
    [Fact]
    public async Task A_refusal_does_not_rehash_a_stale_algorithm()
    {
        await RunAsync(async (provider, identityId) =>
        {
            await ExecuteAsync(
                "UPDATE credential SET password_algorithm = 'pbkdf2-sha256-v0' WHERE user_identity_id = @id",
                identityId);

            var before = await ReadCredentialAsync(identityId);

            Assert.False((await SignInAsync(provider, GoodPassword)).Succeeded);

            var after = await ReadCredentialAsync(identityId);

            Assert.Equal("pbkdf2-sha256-v0", after.PasswordAlgorithm);
            Assert.Equal(before.PasswordHash, after.PasswordHash);
        });
    }

    /// <summary>MP-4: the right password, however often, never locks the account.</summary>
    [Fact]
    public async Task Repeated_refusals_never_lock_the_account()
    {
        await RunAsync(async (provider, identityId) =>
        {
            var before = await LastSequenceAsync();

            for (var i = 0; i < 6; i++)
                Assert.False((await SignInAsync(provider, GoodPassword)).Succeeded);

            var credential = await ReadCredentialAsync(identityId);

            Assert.Equal(0, credential.FailedAttemptCount);
            Assert.Null(credential.LockedUntil);
            Assert.Empty(await RecordsAsync("AccountLocked", identityId, before));
            Assert.Equal(6, (await RecordsAsync("SignInFailed", identityId, before)).Count);
        });
    }

    // --------------------------------------- the other paths are unchanged

    /// <summary>MP-5.</summary>
    [Fact]
    public async Task A_wrong_password_is_still_counted_as_before()
    {
        await RunAsync(async (provider, identityId) =>
        {
            var before = await LastSequenceAsync();

            Assert.False((await SignInAsync(provider, "wrong-password-here")).Succeeded);

            Assert.Equal(1, (await ReadCredentialAsync(identityId)).FailedAttemptCount);

            var failure = Assert.Single(await RecordsAsync("SignInFailed", identityId, before));
            Assert.Contains("\"FailureCategory\": \"CredentialsRejected\"", failure.Payload, StringComparison.Ordinal);
        });
    }

    /// <summary>MP-6: the lock check comes first.</summary>
    [Fact]
    public async Task A_live_lock_is_still_reported_as_locked()
    {
        await RunAsync(async (provider, identityId) =>
        {
            await ExecuteAsync(
                "UPDATE credential SET locked_until = @until, failed_attempt_count = 5 WHERE user_identity_id = @id",
                identityId, ("until", Now.AddMinutes(10)));

            var before = await LastSequenceAsync();

            Assert.False((await SignInAsync(provider, GoodPassword)).Succeeded);

            var failure = Assert.Single(await RecordsAsync("SignInFailed", identityId, before));
            Assert.Contains("\"FailureCategory\": \"AccountLocked\"", failure.Payload, StringComparison.Ordinal);
        });
    }

    /// <summary>The control: with the flag clear, the same password signs in.</summary>
    [Fact]
    public async Task With_the_flag_clear_the_same_password_signs_in()
    {
        await RunAsync(async (provider, identityId) =>
        {
            await ExecuteAsync(
                "UPDATE credential SET must_change_password = false WHERE user_identity_id = @id", identityId);

            Assert.True((await SignInAsync(provider, GoodPassword)).Succeeded);
        });
    }

    // ------------------------------------------------------------- harness

    private static async Task<SignInResult> SignInAsync(IServiceProvider provider, string password)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignInCommand, SignInResult>(
                new SignInCommand(Username, password, "203.0.113.7", "integration-tests/1.0"),
                CancellationToken.None);
    }

    private static async Task RunAsync(Func<IServiceProvider, UserIdentityId, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var caller = await PermanentTestCaller.EnsureAsync(
            TestDatabase.ConnectionString, Label, roleCode: null);

        await ResetAsync(caller.IdentityId);

        try
        {
            var hashed = new PasswordHasher().Hash(GoodPassword);

            await using (var context = CreateContext())
            {
                // The state CRD-C5 leaves: an activated credential whose
                // password is still on file, with a change required.
                context.Add(Credential.Create(
                    CredentialId.New(), caller.IdentityId, IdentityType.Local,
                    hashed.Hash, hashed.Algorithm,
                    passwordChangedAt: Now.AddDays(-30), mustChangePassword: true,
                    createdAt: Now, createdBy: User.SystemUserId));

                await context.SaveChangesAsync(CancellationToken.None);
            }

            var services = new ServiceCollection()
                .AddPlatformApplication()
                .AddPlatformPersistence(TestDatabase.ConnectionString);

            services.AddSingleton<IClock>(new FixedClock(Now));

            await using var provider = services.BuildServiceProvider(validateScopes: true);

            await body(provider, caller.IdentityId);
        }
        finally
        {
            await ResetAsync(caller.IdentityId);
        }
    }

    /// <summary>The rows a run gives the person; the person stays.</summary>
    private static async Task ResetAsync(UserIdentityId identityId)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM password_history WHERE user_identity_id = @id",
            "DELETE FROM user_session WHERE user_identity_id = @id",
            "DELETE FROM credential WHERE user_identity_id = @id",
        })
        {
            await ExecuteAsync(sql, identityId);
        }
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new FixedClock(Now), executionContext: null))
            .Options);

    private static async Task ExecuteAsync(
        string sql, UserIdentityId identityId, params (string Name, object Value)[] parameters)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }

    private sealed record CredentialRow(
        string PasswordHash, string PasswordAlgorithm, string PasswordChangedAt,
        int FailedAttemptCount, DateTimeOffset? LockedUntil, bool MustChangePassword);

    private static async Task<CredentialRow> ReadCredentialAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm, password_changed_at::text,
                   failed_attempt_count, locked_until, must_change_password
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "No credential row.");

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5));
    }

    private static async Task<int> CountSessionsAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_session WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<long> LastSequenceAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(sequence), 0) FROM audit.audit_record", connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record Record(
        string WritePath, string OriginKind, Guid? ActorUserId, string EntityType, string? Payload);

    /// <summary>
    /// Records of one type about this identity, written after a sequence.
    /// SignInFailed and SignInSucceeded name the identity (or its session) as
    /// primary or ref; AccountLocked names the credential, with the identity
    /// as a ref — hence the join over both.
    /// </summary>
    private static async Task<IReadOnlyList<Record>> RecordsAsync(
        string eventType, UserIdentityId identityId, long after)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT r.sequence, r.write_path, r.origin_kind, r.actor_user_id,
                   r.entity_type, r.payload::text
            FROM audit.audit_record r
            LEFT JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type AND r.sequence > @after
              AND (r.entity_id = @id OR e.entity_id = @id)
            ORDER BY r.sequence
            """, connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("id", identityId.Value);

        var records = new List<Record>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new Record(
                reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return records;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
