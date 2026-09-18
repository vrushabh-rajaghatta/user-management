using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// ck_user_role_effective_period, aligned with frozen UR2:
/// EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom (docs/requirements.md,
/// "Role Assignment"). The migrated constraint was stricter (>) than the frozen
/// model, which made the empty period, and with it revoking a future grant,
/// impossible.
///
/// Written with raw SQL on purpose: the domain refuses an empty period for a
/// GRANT, so the only application path to one is revocation. The constraint is
/// what is under test here, not a command.
/// </summary>
public sealed class UserRolePeriodConstraintTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>D1 — the empty period [t, t) is a row the database admits.</summary>
    [Fact]
    public async Task An_empty_period_is_admitted()
    {
        await WithUserAndRoleAsync(async (connection, userId, roleId) =>
        {
            await InsertAsync(connection, userId, roleId, Now.AddDays(10), Now.AddDays(10), revoked: true);

            Assert.Equal(1, await CountAsync(connection, userId));
        });
    }

    /// <summary>D1 — an end before the start is still refused.</summary>
    [Fact]
    public async Task An_end_before_the_start_is_refused()
    {
        await WithUserAndRoleAsync(async (connection, userId, roleId) =>
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(
                () => InsertAsync(connection, userId, roleId, Now.AddDays(10), Now.AddDays(9), revoked: false));

            Assert.Equal("23514", failure.SqlState);
            Assert.Equal("ck_user_role_effective_period", failure.ConstraintName);
        });
    }

    /// <summary>
    /// R2 at the database: an empty period overlaps nothing ('[)' ranges), so a
    /// cancelled future grant does not block a new grant over the same dates.
    /// </summary>
    [Fact]
    public async Task An_empty_period_does_not_block_an_overlapping_grant()
    {
        await WithUserAndRoleAsync(async (connection, userId, roleId) =>
        {
            await InsertAsync(connection, userId, roleId, Now.AddDays(10), Now.AddDays(10), revoked: true);
            await InsertAsync(connection, userId, roleId, Now.AddDays(5), null, revoked: false);

            Assert.Equal(2, await CountAsync(connection, userId));
        });
    }

    // ------------------------------------------------------------ harness

    private static async Task WithUserAndRoleAsync(Func<NpgsqlConnection, UserId, RoleId, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var discriminator = Guid.NewGuid().ToString("N");

        var user = User.CreateHuman(
            UserId.New(), "Period", "Constraint", $"Period Constraint {discriminator[..8]}",
            $"period-{discriminator}@example.test", Now, User.SystemUserId);

        var role = Role.Create(
            RoleId.New(), $"Period Test {discriminator[..8]}", $"period-{discriminator[..16]}",
            "Created by UserRolePeriodConstraintTests.", isSystemRole: false, Now, User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, role);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var connection = await TestDatabase.OpenAsync();

        try
        {
            await body(connection, user.Id, role.Id);
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM user_role WHERE user_id = @user",
                "DELETE FROM app_user WHERE id = @user",
                "DELETE FROM role WHERE id = @role",
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("user", user.Id.Value);
                command.Parameters.AddWithValue("role", role.Id.Value);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection, UserId userId, RoleId roleId,
        DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, bool revoked)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_role
                (id, user_id, actor_type, role_id, scope_type, scope_id,
                 effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                 revoked_at, revoked_by, revocation_reason)
            VALUES
                (@id, @user, 'Human', @role, 'Global', NULL,
                 @from, @to, @assigned, @system, 'Period constraint test.',
                 @revokedAt, @revokedBy, @revocationReason)
            """, connection);

        var system = User.SystemUserId.Value;

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("user", userId.Value);
        command.Parameters.AddWithValue("role", roleId.Value);
        command.Parameters.AddWithValue("from", effectiveFrom);
        command.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        command.Parameters.AddWithValue("assigned", Now);
        command.Parameters.AddWithValue("system", system);
        command.Parameters.AddWithValue("revokedAt", revoked ? Now : DBNull.Value);
        command.Parameters.AddWithValue("revokedBy", revoked ? system : DBNull.Value);
        command.Parameters.AddWithValue("revocationReason", revoked ? "Cancelled before it took effect." : DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, UserId userId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_role WHERE user_id = @user", connection);

        command.Parameters.AddWithValue("user", userId.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);
}
