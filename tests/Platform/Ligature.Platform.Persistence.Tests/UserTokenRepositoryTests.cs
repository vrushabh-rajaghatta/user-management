using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The repository's entire contract is "track this token for the current unit
/// of work", so the tests are correspondingly small. There is no UT4 index to
/// mirror and no pre-check to verify.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class UserTokenRepositoryTests
{

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_tracks_and_UnitOfWork_persists()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserTokenRepository(context);
        var unitOfWork = new UnitOfWork(context);

        var (user, identity, token) = NewActivationToken();

        try
        {
            context.Add(user);
            context.Add(identity);

            await repository.AddAsync(token, CancellationToken.None);

            Assert.Equal(EntityState.Added, context.Entry(token).State);

            // The repository does not save. Nothing exists until the unit of
            // work commits, so a token can never outlive a rolled-back identity.
            Assert.Equal(0, await CountTokensAsync(token.Id));

            await unitOfWork.ExecuteInTransactionAsync(
                _ => Task.FromResult(token.Id),
                CancellationToken.None);

            Assert.Equal(1, await CountTokensAsync(token.Id));
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// The repository persists an already-produced verifier and nothing else.
    /// If the stored value ever differs from what was handed in, something in
    /// this layer has started transforming tokens — which is UserTokenService's
    /// job, not this type's.
    /// </summary>
    [Fact]
    public async Task The_stored_hash_is_exactly_what_was_handed_in()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserTokenRepository(context);
        var unitOfWork = new UnitOfWork(context);

        var (user, identity, token) = NewActivationToken();

        try
        {
            context.Add(user);
            context.Add(identity);

            await repository.AddAsync(token, CancellationToken.None);

            await unitOfWork.ExecuteInTransactionAsync(
                _ => Task.FromResult(token.Id),
                CancellationToken.None);

            var stored = await ReadTokenHashAsync(token.Id);

            Assert.Equal(token.TokenHash, stored, StringComparer.Ordinal);
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    private static (User User, UserIdentity Identity, UserToken Token)
        NewActivationToken()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        var user = User.CreateHuman(
            UserId.New(),
            "Token",
            "Tester",
            $"Token Tester {discriminator[..8]}",
            $"token-{discriminator}@example.test",
            Now,
            User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            $"Token-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        var token = UserToken.Create(
            UserTokenId.New(),
            identity.Id,
            TokenType.Activation,
            tokenHash: $"sha256:{discriminator}",
            createdAt: Now,
            expiresAt: Now.AddHours(72),
            createdBy: User.SystemUserId);

        return (user, identity, token);
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now),
                    executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static async Task<int> CountTokensAsync(UserTokenId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadTokenHashAsync(UserTokenId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT token_hash FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        var value = await command.ExecuteScalarAsync();

        Assert.NotNull(value);

        return (string)value!;
    }

    /// <summary>
    /// Tokens, then identities, then the user — each references the one after.
    /// </summary>
    private static async Task CleanUpAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var tokens = new NpgsqlCommand(
            """
            DELETE FROM user_token
            WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id
            )
            """, connection))
        {
            tokens.Parameters.AddWithValue("id", userId.Value);
            await tokens.ExecuteNonQueryAsync();
        }

        await using (var identities = new NpgsqlCommand(
            "DELETE FROM user_identity WHERE user_id = @id", connection))
        {
            identities.Parameters.AddWithValue("id", userId.Value);
            await identities.ExecuteNonQueryAsync();
        }

        await using var user = new NpgsqlCommand(
            "DELETE FROM app_user WHERE id = @id", connection);

        user.Parameters.AddWithValue("id", userId.Value);
        await user.ExecuteNonQueryAsync();
    }

    private static string ConnectionString => TestDatabase.ConnectionString;


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
