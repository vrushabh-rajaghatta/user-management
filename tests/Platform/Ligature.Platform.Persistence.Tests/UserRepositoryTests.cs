using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// ExistsActiveHumanWithEmailAsync mirrors ux_app_user_active_human_email, and
/// the whole point of mirroring is that the two agree. Only a real PostgreSQL
/// database can show that: an in-memory provider has neither the partial index
/// nor lower() semantics, so it would agree with any predicate it was given.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class UserRepositoryTests
{

    [Fact]
    public async Task AddAsync_tracks_without_saving()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var user = NewHuman();

        try
        {
            await repository.AddAsync(user, CancellationToken.None);

            // The repository must not own the save: nothing is persisted until
            // the unit of work commits, or a partial cascade becomes possible.
            Assert.Equal(0, await CountUsersAsync(user.Id));

            Assert.Equal(
                EntityState.Added,
                context.Entry(user).State);

            await context.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(1, await CountUsersAsync(user.Id));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task An_active_human_with_the_same_email_is_a_conflict()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var user = NewHuman();

        try
        {
            Assert.False(
                await repository.ExistsActiveHumanWithEmailAsync(
                    user.Email!, CancellationToken.None),
                "The address is unused before the user is written.");

            await repository.AddAsync(user, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            Assert.True(
                await repository.ExistsActiveHumanWithEmailAsync(
                    user.Email!, CancellationToken.None));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task The_comparison_is_case_insensitive_like_the_index()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var user = NewHuman();

        try
        {
            await repository.AddAsync(user, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            // AU3 indexes lower("email"), so a differently-cased address is the
            // same address. A case-sensitive check would pass here and then be
            // rejected by the index on INSERT.
            var shouted = EmailAddress.Create(user.Email!.Value.ToUpperInvariant());

            Assert.NotEqual(user.Email.Value, shouted.Value, StringComparer.Ordinal);

            Assert.True(
                await repository.ExistsActiveHumanWithEmailAsync(
                    shouted, CancellationToken.None));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    /// <summary>
    /// The method is named ...ActiveHuman..., but AU3 says "status" &lt;&gt;
    /// 'Inactive'. This is the case that would catch someone "correcting" the
    /// predicate to Status = Active — and, more importantly, it is the spec's
    /// deliberate rule that a departed employee's address may be reissued.
    /// </summary>
    [Fact]
    public async Task An_inactive_human_does_not_hold_their_email_address()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var user = NewHuman();

        try
        {
            await repository.AddAsync(user, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            Assert.True(
                await repository.ExistsActiveHumanWithEmailAsync(
                    user.Email!, CancellationToken.None),
                "Precondition: the address is held while the user is active.");

            user.Deactivate(
                new DeactivationStamp(
                    new DateTimeOffset(2026, 9, 7, 11, 0, 0, TimeSpan.Zero),
                    User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);

            Assert.False(
                await repository.ExistsActiveHumanWithEmailAsync(
                    user.Email!, CancellationToken.None),
                "An inactive holder must not block reuse of the address.");
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    /// <summary>
    /// The pre-check is an affordance, not the guarantee. This is the race it
    /// cannot close: two callers both observe "available", and the database
    /// decides. Item 11 turns 23505 into a business-rule error; until then the
    /// point is simply that the constraint, not the check, is authoritative.
    /// </summary>
    [Fact]
    public async Task The_index_rejects_a_duplicate_the_pre_check_allowed()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var first = NewHuman();
        var second = NewHuman(email: first.Email!.Value.ToUpperInvariant());

        try
        {
            await repository.AddAsync(first, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            await using var racing = CreateContext();
            var racingRepository = new UserRepository(racing);

            await racingRepository.AddAsync(second, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => racing.SaveChangesAsync(CancellationToken.None));

            var postgres = Assert.IsType<PostgresException>(failure.InnerException);

            Assert.Equal("23505", postgres.SqlState);
            Assert.Equal("ux_app_user_active_human_email", postgres.ConstraintName);
        }
        finally
        {
            await DeleteUserAsync(first.Id);
            await DeleteUserAsync(second.Id);
        }
    }

    private static User NewHuman(string? email = null)
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return User.CreateHuman(
            UserId.New(),
            "Repo",
            "Tester",
            $"Repo Tester {discriminator[..8]}",
            email ?? $"Repo-{discriminator}@Example.Test",
            new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero),
            User.SystemUserId);
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(
                        new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero)),
                    executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static async Task<int> CountUsersAsync(UserId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task DeleteUserAsync(UserId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM app_user WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionString => TestDatabase.ConnectionString;


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
