using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Exercises the transactional boundary against a real PostgreSQL database.
/// An in-memory provider would prove nothing here: the questions are whether a
/// real transaction rolls back, whether a second BeginTransaction is avoided,
/// and whether NOT NULL provenance columns are populated — all of which the
/// in-memory provider answers incorrectly by not enforcing them.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class UnitOfWorkTests
{

    [Fact]
    public async Task Commit_persists_every_write_in_the_delegate()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewAdministrator();

        try
        {
            var returned = await unitOfWork.ExecuteInTransactionAsync(
                ct =>
                {
                    context.Add(user);

                    return Task.FromResult(user.Id);
                },
                CancellationToken.None);

            Assert.Equal(user.Id, returned);
            Assert.Equal(1, await CountUsersAsync(user.Id));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task A_throwing_delegate_leaves_nothing_behind()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewAdministrator();

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => unitOfWork.ExecuteInTransactionAsync<UserId>(
                    ct =>
                    {
                        context.Add(user);

                        throw new InvalidOperationException(
                            "The command failed after writing.");
                    },
                    CancellationToken.None));

            Assert.Equal(0, await CountUsersAsync(user.Id));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task An_existing_transaction_is_joined_rather_than_replaced()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewAdministrator();

        await using var outer = await context.Database
            .BeginTransactionAsync(CancellationToken.None);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct =>
                {
                    context.Add(user);

                    return Task.FromResult(user.Id);
                },
                CancellationToken.None);

            // Still the caller's transaction: the unit of work neither opened
            // a second one nor committed the one it was handed.
            Assert.Same(outer, context.Database.CurrentTransaction);

            await outer.RollbackAsync(CancellationToken.None);

            Assert.Equal(0, await CountUsersAsync(user.Id));
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task Provenance_columns_are_stamped_without_an_execution_context()
    {
        await TestDatabase.EnsureReachableAsync();

        var clock = new FixedClock(
            new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero));

        await using var context = CreateContext(clock);
        var unitOfWork = new UnitOfWork(context);

        var user = NewAdministrator();

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct =>
                {
                    context.Add(user);

                    return Task.FromResult(user.Id);
                },
                CancellationToken.None);

            var (updatedAt, updatedBy) = await ReadProvenanceAsync(user.Id);

            Assert.Equal(clock.UtcNow, updatedAt);
            Assert.Equal(User.SystemUserId.Value, updatedBy);
            Assert.NotEqual(Guid.Empty, updatedBy);
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    /// <summary>
    /// CreatedAt and UpdatedAt answer different questions — when the operation
    /// decided to create the row, and when EF persisted it — so they are NOT
    /// required to be byte-identical. What matters is that both are real,
    /// ordered timestamps rather than a CLR default that slipped through.
    /// </summary>
    [Fact]
    public async Task Created_and_updated_timestamps_are_both_valid_without_being_equal()
    {
        await TestDatabase.EnsureReachableAsync();

        var createdAt = new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);

        // A save-time clock deliberately later than the operation timestamp,
        // mirroring what SystemClock does in production.
        var clock = new FixedClock(createdAt.AddMilliseconds(4));

        await using var context = CreateContext(clock);
        var unitOfWork = new UnitOfWork(context);

        var user = NewAdministrator(createdAt);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct =>
                {
                    context.Add(user);

                    return Task.FromResult(user.Id);
                },
                CancellationToken.None);

            var (storedCreatedAt, storedUpdatedAt) =
                await ReadTimestampsAsync(user.Id);

            Assert.NotEqual(default, storedCreatedAt);
            Assert.NotEqual(default, storedUpdatedAt);

            Assert.Equal(createdAt, storedCreatedAt);
            Assert.Equal(clock.UtcNow, storedUpdatedAt);

            // Persisted after it was created, never before.
            Assert.True(
                storedUpdatedAt >= storedCreatedAt,
                $"updated_at {storedUpdatedAt:O} precedes created_at "
                + $"{storedCreatedAt:O}.");
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    private static User NewAdministrator(DateTimeOffset? createdAt = null)
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return User.CreateHuman(
            UserId.New(),
            "Unit",
            "Ofwork",
            $"Unit Ofwork {discriminator[..8]}",
            $"uow-{discriminator}@example.test",
            createdAt
                ?? new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero),
            User.SystemUserId);
    }

    private static LigatureDbContext CreateContext(IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    clock ?? new FixedClock(DateTimeOffset.UtcNow),
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

    private static async Task<(DateTimeOffset UpdatedAt, Guid UpdatedBy)>
        ReadProvenanceAsync(UserId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT updated_at, updated_by FROM app_user WHERE id = @id",
            connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The user row was not written.");

        return (reader.GetFieldValue<DateTimeOffset>(0), reader.GetGuid(1));
    }

    private static async Task<(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>
        ReadTimestampsAsync(UserId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT created_at, updated_at FROM app_user WHERE id = @id",
            connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The user row was not written.");

        return (
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1));
    }

    /// <summary>
    /// G1 forbids the application role from deleting, but these rows are test
    /// residue rather than regulatory records, and the test connection is not
    /// the application role.
    /// </summary>
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
