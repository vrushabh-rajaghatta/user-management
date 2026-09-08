using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
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
/// CRD-C1 end to end: no authenticated caller, a real token, the real adapters,
/// dispatch through the pipeline, and the rows read back from PostgreSQL.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class ActivateAccountIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private const string GoodPassword = "a-sufficiently-long-password";

    [Fact]
    public async Task A_valid_token_creates_the_credential_and_its_history()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var result = await dispatcher
                .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                    CancellationToken.None);

            Assert.Equal(fixture.IdentityId, result.UserIdentityId);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal("Local", credential.IdentityType);
            Assert.False(credential.MustChangePassword);
            Assert.Equal(0, credential.FailedAttemptCount);
            Assert.Null(credential.LockedUntil);

            // Nobody is authenticated, so the row is the System actor's.
            Assert.Equal(User.SystemUserId.Value, credential.CreatedBy);

            // CR5 — history written in the same transaction, or the reuse
            // check has a permanent hole from the very first password.
            var history = await ReadHistoryAsync(fixture.IdentityId);

            Assert.Equal(credential.PasswordHash, history.PasswordHash);
            Assert.Equal(credential.PasswordAlgorithm, history.PasswordAlgorithm);

            // The token is spent.
            Assert.NotNull(await ReadTokenUsedAtAsync(fixture.TokenId));
        });
    }

    /// <summary>
    /// The catalogue's named failure mode: two simultaneous clicks. The second
    /// must lose, and it must lose on the conditional UPDATE rather than on a
    /// prior read.
    /// </summary>
    [Fact]
    public async Task A_token_cannot_be_consumed_twice()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            var failure = await Assert
                .ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                            new ActivateAccountCommand(
                                fixture.TokenPlainText, "another-long-password"),
                            CancellationToken.None));

            Assert.Equal("The activation token is not valid.", failure.Message);

            // And the replay left no second credential behind.
            Assert.Equal(1, await CountCredentialsAsync(fixture.IdentityId));
            Assert.Equal(1, await CountHistoryAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// The nasty case this story exists to prevent. A password below the policy
    /// floor is rejected AFTER the token has been consumed inside the
    /// transaction — so the consumption must roll back with it, or the user is
    /// permanently unable to activate because of a typo.
    /// </summary>
    [Fact]
    public async Task A_rejected_password_leaves_the_token_usable()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher
                    .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                        new ActivateAccountCommand(fixture.TokenPlainText, "short"),
                        CancellationToken.None));

            Assert.Null(await ReadTokenUsedAtAsync(fixture.TokenId));
            Assert.Equal(0, await CountCredentialsAsync(fixture.IdentityId));
            Assert.Equal(0, await CountHistoryAsync(fixture.IdentityId));

            // The same token still works, which is the whole point.
            var result = await dispatcher
                .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                    CancellationToken.None);

            Assert.Equal(fixture.IdentityId, result.UserIdentityId);
            Assert.NotNull(await ReadTokenUsedAtAsync(fixture.TokenId));
        });
    }

    [Fact]
    public async Task A_password_below_the_effective_floor_is_refused()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var floor = SecurityBaseline.Current.PasswordMinLength;

            var failure = await Assert
                .ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                            new ActivateAccountCommand(
                                fixture.TokenPlainText, new string('x', floor - 1)),
                            CancellationToken.None));

            Assert.Contains(floor.ToString(), failure.Message);

            // Exactly at the floor is acceptable.
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(
                    fixture.TokenPlainText, new string('x', floor)),
                CancellationToken.None);

            Assert.Equal(1, await CountCredentialsAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// Six different reasons, one message. Anything else turns the activation
    /// endpoint into a token-state oracle.
    /// </summary>
    [Fact]
    public async Task Every_invalid_token_gives_the_same_answer()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            // ck_user_token_expires_after_created still applies: an expired
            // token is one issued before it lapsed, not one that never made
            // sense.
            var expired = await IssueTokenAsync(
                fixture.IdentityId,
                expiresAt: Now.AddDays(-1),
                createdAt: Now.AddDays(-2));

            var invalidated = await IssueTokenAsync(
                fixture.IdentityId, invalidatedAt: Now);

            var candidates = new[]
            {
                ("unknown id", $"{Guid.NewGuid()}.{fixture.Secret}"),
                ("wrong secret", $"{fixture.TokenId.Value}.wrong-secret-value"),
                ("expired", expired),
                ("invalidated", invalidated),
                ("malformed", "not-a-token"),
                ("empty secret", $"{fixture.TokenId.Value}."),
            };

            foreach (var (reason, token) in candidates)
            {
                var failure = await Assert
                    .ThrowsAsync<BusinessRuleViolationException>(
                        () => dispatcher
                            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                                new ActivateAccountCommand(token, GoodPassword),
                                CancellationToken.None));

                Assert.Equal(
                    "The activation token is not valid.", failure.Message);

                Assert.Equal(
                    0, await CountCredentialsAsync(fixture.IdentityId));
            }
        });
    }

    [Fact]
    public async Task The_plaintext_password_is_never_persisted()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            foreach (var dump in new[]
            {
                await DumpRowAsync("credential", fixture.IdentityId),
                await DumpRowAsync("password_history", fixture.IdentityId),
            })
            {
                Assert.DoesNotContain(
                    GoodPassword, dump, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Fixture(
        UserId UserId,
        UserIdentityId IdentityId,
        UserTokenId TokenId,
        string Secret,
        string TokenPlainText);

    private static async Task RunAsync(
        Func<ICommandDispatcher, Fixture, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var fixture = await SeedAsync();

        try
        {
            await using var provider = BuildProvider();
            using var scope = provider.CreateScope();

            // Deliberately NO execution context is established: CRD-C1 runs
            // anonymously, and the pipeline must let it through.
            await body(
                scope.ServiceProvider.GetRequiredService<ICommandDispatcher>(),
                fixture);
        }
        finally
        {
            await DeleteActorAsync(fixture.UserId);
        }
    }

    private static ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(TestDatabase.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    /// <summary>
    /// A user with a local identity and no credential — exactly the state
    /// USR-C1 leaves behind (inv. 15).
    /// </summary>
    private static async Task<Fixture> SeedAsync()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        var user = User.CreateHuman(
            UserId.New(),
            "Pending",
            "Activation",
            $"Pending Activation {discriminator[..8]}",
            $"activate-{discriminator}@example.test",
            Now,
            User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            $"activate-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId,
            identity.Id,
            TokenType.Activation,
            material.Hash,
            Now,
            Now + SecurityBaseline.Current.ActivationTokenLifetime,
            User.SystemUserId);

        await using var context = CreateContext();
        context.AddRange(user, identity, token);
        await context.SaveChangesAsync(CancellationToken.None);

        var separator = material.PlainText.IndexOf('.');

        return new Fixture(
            user.Id,
            identity.Id,
            tokenId,
            material.PlainText[(separator + 1)..],
            material.PlainText);
    }

    /// <summary>
    /// Issues an extra token in a chosen terminal state, returning its
    /// delivered form.
    /// </summary>
    private static async Task<string> IssueTokenAsync(
        UserIdentityId identityId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? invalidatedAt = null,
        DateTimeOffset? createdAt = null)
    {
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_token
                (id, user_identity_id, token_type, token_hash, expires_at,
                 used_at, invalidated_at, created_at, created_by)
            VALUES
                (@id, @identityId, 'Activation', @hash, @expiresAt,
                 NULL, @invalidatedAt, @now, @system)
            """, connection);

        command.Parameters.AddWithValue("id", tokenId.Value);
        command.Parameters.AddWithValue("identityId", identityId.Value);
        command.Parameters.AddWithValue("hash", material.Hash);
        command.Parameters.AddWithValue("expiresAt", expiresAt ?? Now.AddDays(1));
        command.Parameters.AddWithValue(
            "invalidatedAt", (object?)invalidatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("now", createdAt ?? Now);
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();

        return material.PlainText;
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

    private sealed record CredentialRow(
        string IdentityType, string PasswordHash, string PasswordAlgorithm,
        bool MustChangePassword, int FailedAttemptCount,
        DateTimeOffset? LockedUntil, Guid CreatedBy);

    private static async Task<CredentialRow> ReadCredentialAsync(
        UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT identity_type, password_hash, password_algorithm,
                   must_change_password, failed_attempt_count, locked_until,
                   created_by
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No credential row was written.");

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetBoolean(3), reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetGuid(6));
    }

    private sealed record HistoryRow(string PasswordHash, string PasswordAlgorithm);

    private static async Task<HistoryRow> ReadHistoryAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm
            FROM password_history WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No history row was written.");

        return new HistoryRow(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<DateTimeOffset?> ReadTokenUsedAtAsync(UserTokenId id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT used_at FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        var value = await command.ExecuteScalarAsync();

        // Npgsql surfaces timestamptz as DateTime through the untyped scalar.
        return value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };
    }

    private static async Task<string> DumpRowAsync(
        string table, UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT {table}::text FROM {table} WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static Task<int> CountCredentialsAsync(UserIdentityId identityId)
        => CountAsync("credential", identityId);

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

    private static async Task DeleteActorAsync(UserId userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            """
            DELETE FROM password_history WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            """
            DELETE FROM credential WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            """
            DELETE FROM user_token WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
