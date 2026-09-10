using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C1 end to end: no authenticated caller, real adapters, dispatch through
/// the pipeline, rows read back from PostgreSQL.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class SignInIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private const string GoodPassword = "a-sufficiently-long-password";

    // ------------------------------------------------------------- success

    [Fact]
    public async Task Correct_credentials_create_a_session()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var result = await SignInAsync(provider, fixture.Username, GoodPassword);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.SessionId);

            var session = await ReadSessionAsync(result.SessionId!);

            Assert.Equal(fixture.IdentityId.Value, session.UserIdentityId);
            Assert.Null(session.RevokedAt);

            // Step 7 — absolute lifetime from the effective policy, a CAP.
            Assert.Equal(
                SecurityBaseline.Current.SessionAbsoluteTimeout,
                session.ExpiresAt - session.CreatedAt);

            // Starts its idle window at creation.
            Assert.Equal(session.CreatedAt, session.LastActivityAt);

            // Security evidence.
            Assert.Equal("203.0.113.7", session.IpAddress);
            Assert.Equal("integration-tests/1.0", session.UserAgent);
        });
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failure_counters()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await SignInAsync(provider, fixture.Username, "wrong-password-here");
            await SignInAsync(provider, fixture.Username, "wrong-password-here");

            Assert.Equal(2, (await ReadCredentialAsync(fixture.IdentityId)).FailedAttemptCount);

            Assert.True(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal(0, credential.FailedAttemptCount);
            Assert.Null(credential.LockedUntil);
        });
    }

    // ------------------------------------------ the durability of a failure

    /// <summary>
    /// THE architectural test for this story.
    ///
    /// A failed sign-in returns Succeeded = false rather than throwing,
    /// precisely so the transaction commits the incremented counter. If someone
    /// later "tidies" the handler by throwing on a wrong password, the counter
    /// rolls back, lockout never engages, and brute force becomes free — while
    /// every happy-path test still passes.
    ///
    /// The state is therefore read back through a FRESH DbContext, not the one
    /// the command used: a tracked in-memory entity would look correct even if
    /// nothing had been committed.
    /// </summary>
    [Fact]
    public async Task A_failed_sign_in_commits_its_counter_despite_reporting_failure()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var result = await SignInAsync(
                provider, fixture.Username, "wrong-password-here");

            Assert.False(result.Succeeded);
            Assert.Null(result.SessionId);

            await using var fresh = CreateContext();

            var credential = await fresh.Set<Credential>()
                .AsNoTracking()
                .FirstAsync(x => x.UserIdentityId == fixture.IdentityId);

            Assert.Equal(1, credential.FailedAttemptCount);

            // And no session was created for a failed attempt.
            Assert.Equal(0, await CountSessionsAsync(fixture.IdentityId));
        });
    }

    // -------------------------------------------------------------- lockout

    [Fact]
    public async Task Reaching_the_threshold_locks_the_account()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var threshold = SecurityBaseline.Current.MaxFailedLoginAttempts;

            for (var attempt = 1; attempt <= threshold; attempt++)
            {
                Assert.False(
                    (await SignInAsync(provider, fixture.Username, "wrong-password"))
                        .Succeeded);
            }

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal(threshold, credential.FailedAttemptCount);
            Assert.NotNull(credential.LockedUntil);

            Assert.Equal(
                SecurityBaseline.Current.LockoutDuration,
                credential.LockedUntil!.Value - Now);
        });
    }

    /// <summary>
    /// A live lock refuses the CORRECT password. Without it, lockout would be
    /// decorative.
    /// </summary>
    [Fact]
    public async Task A_locked_account_refuses_even_the_right_password()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await LockAsync(fixture.IdentityId, until: Now.AddMinutes(15));

            Assert.False(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);

            Assert.Equal(0, await CountSessionsAsync(fixture.IdentityId));
        });
    }

    [Fact]
    public async Task An_expired_lock_lets_the_right_password_through_and_resets()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await LockAsync(fixture.IdentityId, until: Now.AddMinutes(-1), count: 5);

            Assert.True(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Null(credential.LockedUntil);
            Assert.Equal(0, credential.FailedAttemptCount);
        });
    }

    /// <summary>
    /// The pathological case the expired-lock reset exists to prevent: without
    /// it the counter would still sit at the threshold, and this single wrong
    /// password would re-lock the account immediately for another full
    /// duration — one typo costing another lockout, indefinitely.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_after_an_expired_lock_starts_a_new_window()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await LockAsync(fixture.IdentityId, until: Now.AddMinutes(-1), count: 5);

            Assert.False(
                (await SignInAsync(provider, fixture.Username, "wrong-password"))
                    .Succeeded);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal(1, credential.FailedAttemptCount);
            Assert.Null(credential.LockedUntil);
        });
    }

    // -------------------------------------------------- enumeration resistance

    /// <summary>
    /// Five reasons, one outcome, no exception. The command must not let a
    /// caller learn whether an account exists, is active, or has been activated.
    /// </summary>
    [Fact]
    public async Task Every_unauthenticatable_state_gives_the_same_answer()
    {
        await RunAsync(async (provider, fixture) =>
        {
            // Unknown username.
            await AssertRefusedAsync(provider, $"nobody-{Guid.NewGuid():N}", GoodPassword);

            // Correct username, wrong password.
            await AssertRefusedAsync(provider, fixture.Username, "wrong-password");

            // The email address is not a username (never a lookup key).
            await AssertRefusedAsync(provider, fixture.Email, GoodPassword);

            // Inactive identity.
            await SetIdentityStatusAsync(fixture.IdentityId, "Inactive");
            await AssertRefusedAsync(provider, fixture.Username, GoodPassword);
            await SetIdentityStatusAsync(fixture.IdentityId, "Active");

            // Inactive user.
            await SetUserStatusAsync(fixture.UserId, "Inactive");
            await AssertRefusedAsync(provider, fixture.Username, GoodPassword);
            await SetUserStatusAsync(fixture.UserId, "Active");

            Assert.Equal(0, await CountSessionsAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// Inv. 15 — a user created by USR-C1 has no credential until activation,
    /// and absence IS the pending state. It must refuse like any other failure.
    /// </summary>
    [Fact]
    public async Task An_unactivated_account_refuses_generically()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await DeleteCredentialAsync(fixture.IdentityId);

            await AssertRefusedAsync(provider, fixture.Username, GoodPassword);

            Assert.Equal(0, await CountSessionsAsync(fixture.IdentityId));
        });
    }

    // --------------------------------------------------------------- rehash

    /// <summary>
    /// Step 5. The stored representation is replaced on successful sign-in when
    /// the algorithm is stale — but the password did not change, so
    /// PasswordChangedAt must not move and no history row may appear.
    /// </summary>
    [Fact]
    public async Task A_stale_algorithm_is_rehashed_without_looking_like_a_change()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var before = await ReadCredentialAsync(fixture.IdentityId);
            var historyBefore = await CountHistoryAsync(fixture.IdentityId);

            await MarkAlgorithmStaleAsync(fixture.IdentityId);

            Assert.True(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);

            var after = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal(PasswordHasher.AlgorithmMarker, after.PasswordAlgorithm);
            Assert.NotEqual(before.PasswordHash, after.PasswordHash);

            // The password did not change.
            Assert.Equal(before.PasswordChangedAt, after.PasswordChangedAt);

            // CR5 covers password SET and CHANGE. A rehash is neither, and a
            // row here would record the same password twice and shorten the
            // reuse window.
            Assert.Equal(historyBefore, await CountHistoryAsync(fixture.IdentityId));

            // The new representation still authenticates.
            Assert.True(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);
        });
    }

    [Fact]
    public async Task A_current_algorithm_is_left_alone()
    {
        await RunAsync(async (provider, fixture) =>
        {
            var before = await ReadCredentialAsync(fixture.IdentityId);

            Assert.True(
                (await SignInAsync(provider, fixture.Username, GoodPassword))
                    .Succeeded);

            Assert.Equal(
                before.PasswordHash,
                (await ReadCredentialAsync(fixture.IdentityId)).PasswordHash);
        });
    }

    // ------------------------------------------------------------ integrity

    /// <summary>
    /// A corrupted stored hash is a fault in our own data, not a wrong
    /// password. It must surface rather than being absorbed into the generic
    /// refusal, or the corruption stays invisible while a user is locked out.
    /// </summary>
    [Fact]
    public async Task A_corrupted_stored_hash_surfaces_rather_than_refusing()
    {
        await RunAsync(async (provider, fixture) =>
        {
            await CorruptHashAsync(fixture.IdentityId);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => SignInAsync(provider, fixture.Username, GoodPassword));
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Fixture(
        UserId UserId, UserIdentityId IdentityId, string Username, string Email);

    /// <summary>
    /// Each sign-in runs in its OWN DI scope, as it would behind a host: one
    /// command, one scope, one DbContext. Sharing a scope across attempts would
    /// let EF return the identity it already tracks, so a status change made
    /// between attempts would be invisible to the handler and the test would
    /// pass for the wrong reason.
    /// </summary>
    private static async Task<SignInResult> SignInAsync(
        IServiceProvider provider, string username, string password)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignInCommand, SignInResult>(
                new SignInCommand(
                    username, password, "203.0.113.7", "integration-tests/1.0"),
                CancellationToken.None);
    }

    private static async Task AssertRefusedAsync(
        IServiceProvider provider, string username, string password)
    {
        var result = await SignInAsync(provider, username, password);

        Assert.False(result.Succeeded);
        Assert.Null(result.SessionId);
    }

    private static async Task RunAsync(
        Func<IServiceProvider, Fixture, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var fixture = await SeedAsync();

        try
        {
            await using var provider = BuildProvider();

            // No execution context is ever established: SES-C1 is what
            // produces an authenticated caller, so it cannot require one. This
            // is CRD-C1's anonymous marker being reused, not a second path.
            await body(provider, fixture);
        }
        finally
        {
            // The person stays. A successful sign-in makes them the actor of a
            // record, and an actor cannot be deleted; what this run gave them
            // — a credential and any sessions — goes.
            await ResetAsync(fixture.IdentityId);
        }
    }

    /// <summary>
    /// The clock is pinned so the fixed timestamps these tests assert against
    /// are the ones the handler sees. Registered after the platform modules so
    /// it replaces SystemClock.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(TestDatabase.ConnectionString);

        services.AddSingleton<IClock>(new FixedClock(Now));

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// An active user with an active local identity and an activated
    /// credential — the state CRD-C1 leaves behind.
    /// </summary>
    /// <summary>
    /// A permanent person with a credential of this run's own.
    ///
    /// The person is permanent because SES-C1 now records a successful sign-in
    /// and the actor of a record cannot be deleted. Everything that makes the
    /// person signable-in-to is not: the credential is recreated each run, so
    /// the lockout counters, the stored hash and the algorithm start where each
    /// test expects them rather than where the last one left them. The ensure
    /// also puts both statuses back to Active, which matters because tests here
    /// deactivate them.
    /// </summary>
    private static async Task<Fixture> SeedAsync()
    {
        const string label = "signin";

        var caller = await PermanentTestCaller.EnsureAsync(
            TestDatabase.ConnectionString, label, roleCode: null);

        await ResetAsync(caller.IdentityId);

        var username = $"permanent-{label}";
        var email = $"permanent-{label}@example.test";

        var hashed = new PasswordHasher().Hash(GoodPassword);

        var credential = Credential.Create(
            CredentialId.New(), caller.IdentityId, IdentityType.Local,
            hashed.Hash, hashed.Algorithm,
            // Deliberately older than Now: if a rehash wrongly went through
            // ChangePassword, PasswordChangedAt would jump to the sign-in
            // instant, and a fixture that shared the timestamp would hide it.
            passwordChangedAt: Now.AddDays(-30), mustChangePassword: false,
            createdAt: Now, createdBy: User.SystemUserId);

        await using var context = CreateContext();
        context.Add(credential);
        await context.SaveChangesAsync(CancellationToken.None);

        return new Fixture(caller.UserId, caller.IdentityId, username, email);
    }

    /// <summary>
    /// The rows a run gives the fixture, and only those. Nothing here is
    /// referenced by an audit record: a session is not an actor, and the
    /// records of a sign-in point at the person, who stays.
    /// </summary>
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

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    // ------------------------------------------------------------- readers

    private sealed record SessionRow(
        Guid UserIdentityId, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt,
        DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt,
        string? IpAddress, string? UserAgent);

    private static async Task<SessionRow> ReadSessionAsync(UserSessionId id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT user_identity_id, created_at, last_activity_at, expires_at,
                   revoked_at, host(ip_address), user_agent
            FROM user_session WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No session row was written.");

        return new SessionRow(
            reader.GetGuid(0),
            Offset(reader.GetValue(1))!.Value,
            Offset(reader.GetValue(2))!.Value,
            Offset(reader.GetValue(3))!.Value,
            reader.IsDBNull(4) ? null : Offset(reader.GetValue(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private sealed record CredentialRow(
        string PasswordHash, string PasswordAlgorithm,
        DateTimeOffset PasswordChangedAt, int FailedAttemptCount,
        DateTimeOffset? LockedUntil);

    private static async Task<CredentialRow> ReadCredentialAsync(
        UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm, password_changed_at,
                   failed_attempt_count, locked_until
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No credential row.");

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1),
            Offset(reader.GetValue(2))!.Value, reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Offset(reader.GetValue(4)));
    }

    private static DateTimeOffset? Offset(object? value)
        => value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };

    // ------------------------------------------------------------- mutators

    private static Task LockAsync(
        UserIdentityId identityId, DateTimeOffset until, int count = 5)
        => ExecuteAsync(
            """
            UPDATE credential
            SET locked_until = @until, failed_attempt_count = @count
            WHERE user_identity_id = @id
            """,
            identityId.Value, ("until", until), ("count", count));

    private static Task MarkAlgorithmStaleAsync(UserIdentityId identityId)
        => ExecuteAsync(
            "UPDATE credential SET password_algorithm = @value WHERE user_identity_id = @id",
            identityId.Value, ("value", "pbkdf2-sha256-v0"));

    private static Task CorruptHashAsync(UserIdentityId identityId)
        => ExecuteAsync(
            "UPDATE credential SET password_hash = @value WHERE user_identity_id = @id",
            identityId.Value, ("value", "this-is-not-a-valid-hash"));

    private static Task SetIdentityStatusAsync(
        UserIdentityId identityId, string status)
        => ExecuteAsync(
            "UPDATE user_identity SET status = @value WHERE id = @id",
            identityId.Value, ("value", status));

    private static Task SetUserStatusAsync(UserId userId, string status)
        => ExecuteAsync(
            "UPDATE app_user SET status = @value WHERE id = @id",
            userId.Value, ("value", status));

    private static Task DeleteCredentialAsync(UserIdentityId identityId)
        => ExecuteAsync(
            "DELETE FROM credential WHERE user_identity_id = @id",
            identityId.Value);

    private static async Task ExecuteAsync(
        string sql, Guid id, params (string Name, object Value)[] parameters)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }

    private static Task<int> CountSessionsAsync(UserIdentityId identityId)
        => CountAsync("user_session", identityId);

    private static Task<int> CountHistoryAsync(UserIdentityId identityId)
        => CountAsync("password_history", identityId);

    private static async Task<int> CountAsync(
        string table, UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
