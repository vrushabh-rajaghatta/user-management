using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The two readers behind AUT-Q2 and the grantable-role list, against real
/// PostgreSQL (docs/requirements.md, "AUT-Q2"). What only the database can
/// prove: which rows belong, the names joined in, and the role list's filter
/// and order. The state is NOT the reader's business, and its records carry
/// none.
///
/// Seeded with raw SQL and removed afterwards: nothing here writes audit.
/// </summary>
public sealed class RoleAssignmentReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // ================================================================ assignments

    [Fact]
    public async Task Every_assignment_of_the_user_is_read_with_its_names_and_provenance()
    {
        await WithSeedAsync(async seed =>
        {
            var granted = await InsertAssignmentAsync(seed, seed.RoleA, Now.AddDays(-10), null, revoked: false);
            var revoked = await InsertAssignmentAsync(seed, seed.RoleB, Now.AddDays(-20), Now.AddDays(-5), revoked: true);

            var records = await ReadAssignmentsAsync(seed.Target);

            Assert.NotNull(records);
            Assert.Equal(2, records.Count);

            var active = Assert.Single(records, x => x.AssignmentId.Value == granted);
            Assert.Equal(seed.RoleA.Value, active.RoleId.Value);
            Assert.Equal(seed.RoleAName, active.RoleName);
            Assert.Equal(seed.Granter.Value, active.AssignedBy.UserId.Value);
            Assert.Equal(seed.GranterName, active.AssignedBy.DisplayName);
            Assert.Equal("Granted for the reader test.", active.AssignmentReason);
            Assert.Null(active.EffectiveTo);
            Assert.Null(active.RevokedAt);
            Assert.Null(active.RevokedBy);
            Assert.Null(active.RevocationReason);

            var ended = Assert.Single(records, x => x.AssignmentId.Value == revoked);
            Assert.NotNull(ended.RevokedAt);
            Assert.Equal(seed.Granter.Value, ended.RevokedBy!.UserId.Value);
            Assert.Equal(seed.GranterName, ended.RevokedBy.DisplayName);
            Assert.Equal("Revoked for the reader test.", ended.RevocationReason);
        });
    }

    [Fact]
    public async Task Another_users_assignments_are_not_read()
    {
        await WithSeedAsync(async seed =>
        {
            await InsertAssignmentAsync(seed, seed.RoleA, Now, null, revoked: false, holder: seed.Granter);

            var records = await ReadAssignmentsAsync(seed.Target);

            Assert.NotNull(records);
            Assert.Empty(records);
        });
    }

    /// <summary>A user with no assignment is an empty list; an unknown user is null.</summary>
    [Fact]
    public async Task An_unknown_user_reads_as_null()
    {
        await TestDatabase.EnsureProvisionedAsync();

        Assert.Null(await ReadAssignmentsAsync(UserId.New()));
    }

    // ================================================================ roles

    [Fact]
    public async Task Only_active_roles_are_grantable_with_exactly_their_fields()
    {
        await WithSeedAsync(async seed =>
        {
            var roles = await ReadRolesAsync();

            var active = Assert.Single(roles, x => x.RoleId.Value == seed.RoleA.Value);
            Assert.Equal(seed.RoleAName, active.Name);
            Assert.Equal("A role for the reader test.", active.Description);

            Assert.DoesNotContain(roles, x => x.RoleId.Value == seed.RetiredRole.Value);
        });
    }

    /// <summary>
    /// ICU "unicode", as USR-Q1: "de la Cruz" / "Delacroix" order differently
    /// under this database's default collation, so omitting the collation
    /// cannot pass.
    /// </summary>
    [Fact]
    public async Task Roles_are_ordered_by_name_under_icu_unicode()
    {
        await WithSeedAsync(async seed =>
        {
            var names = (await ReadRolesAsync())
                .Select(x => x.Name)
                .Where(x => x.EndsWith(seed.Marker, StringComparison.Ordinal))
                .ToList();

            Assert.Equal(
                [$"de la Cruz {seed.Marker}", $"Delacroix {seed.Marker}"],
                names);
        }, orderingPair: true);
    }

    // ================================================================ harness

    private sealed record Seed(
        string Marker, UserId Target, UserId Granter, string GranterName,
        RoleId RoleA, string RoleAName, RoleId RoleB, RoleId RetiredRole, List<Guid> ExtraRoles);

    private static async Task WithSeedAsync(Func<Seed, Task> body, bool orderingPair = false)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var marker = Guid.NewGuid().ToString("N")[..10];
        var target = Guid.NewGuid();
        var granter = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var retired = Guid.NewGuid();
        var extra = new List<Guid>();
        var granterName = $"Granter {marker}";
        var roleAName = $"Reader Role A {marker}";

        await using var connection = await TestDatabase.OpenAsync();

        await ExecuteAsync(connection, $"""
            INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                  created_at, created_by, updated_at, updated_by)
            VALUES ('{target}', 'Human', 'Reader', 'Target', 'Reader Target {marker}', 'reader-target-{marker}@example.test',
                    'Active', now(), '{System}', now(), '{System}'),
                   ('{granter}', 'Human', 'Reader', 'Granter', '{granterName}', 'reader-granter-{marker}@example.test',
                    'Active', now(), '{System}', now(), '{System}');
            """);

        await InsertRoleAsync(connection, roleA, roleAName, $"reader-a-{marker}", true, "A role for the reader test.");
        await InsertRoleAsync(connection, roleB, $"Reader Role B {marker}", $"reader-b-{marker}", true, null);
        await InsertRoleAsync(connection, retired, $"Reader Retired {marker}", $"reader-r-{marker}", false, null);

        if (orderingPair)
        {
            foreach (var name in new[] { $"Delacroix {marker}", $"de la Cruz {marker}" })
            {
                var id = Guid.NewGuid();
                extra.Add(id);
                await InsertRoleAsync(connection, id, name, $"reader-o-{extra.Count}-{marker}", true, null);
            }
        }

        var seed = new Seed(
            marker, new UserId(target), new UserId(granter), granterName,
            new RoleId(roleA), roleAName, new RoleId(roleB), new RoleId(retired), extra);

        try
        {
            await body(seed);
        }
        finally
        {
            await ExecuteAsync(connection, $"""
                DELETE FROM user_role WHERE user_id IN ('{target}', '{granter}');
                DELETE FROM app_user WHERE id IN ('{target}', '{granter}');
                DELETE FROM role WHERE id IN ('{roleA}', '{roleB}', '{retired}'{string.Concat(extra.Select(x => $", '{x}'"))});
                """);
        }
    }

    private static Guid System => User.SystemUserId.Value;

    private static async Task InsertRoleAsync(
        NpgsqlConnection connection, Guid id, string name, string code, bool active, string? description)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO role (id, name, code, description, is_system_role, is_active, created_at, created_by, updated_at, updated_by)
            VALUES (@id, @name, @code, @description, false, @active, now(), @system, now(), @system)
            """, connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("system", System);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertAssignmentAsync(
        Seed seed, RoleId role, DateTimeOffset from, DateTimeOffset? to, bool revoked, UserId? holder = null)
    {
        var id = Guid.NewGuid();

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_role
                (id, user_id, actor_type, role_id, scope_type, scope_id,
                 effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                 revoked_at, revoked_by, revocation_reason)
            VALUES
                (@id, @user, 'Human', @role, 'Global', NULL,
                 @from, @to, @from, @granter, 'Granted for the reader test.',
                 @revokedAt, @revokedBy, @reason)
            """, connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", (holder ?? seed.Target).Value);
        command.Parameters.AddWithValue("role", role.Value);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", (object?)to ?? DBNull.Value);
        command.Parameters.AddWithValue("granter", seed.Granter.Value);
        command.Parameters.AddWithValue("revokedAt", revoked ? (object)to!.Value : DBNull.Value);
        command.Parameters.AddWithValue("revokedBy", revoked ? seed.Granter.Value : DBNull.Value);
        command.Parameters.AddWithValue("reason", revoked ? "Revoked for the reader test." : DBNull.Value);

        await command.ExecuteNonQueryAsync();

        return id;
    }

    private static async Task<IReadOnlyList<Application.Users.Queries.RoleAssignments.UserRoleAssignmentRecord>?>
        ReadAssignmentsAsync(UserId userId)
    {
        await using var context = CreateContext();

        return await new UserRoleAssignmentReader(context).ReadAsync(userId, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<Application.Users.Queries.GrantableRoles.GrantableRole>> ReadRolesAsync()
    {
        await using var context = CreateContext();

        return await new GrantableRoleReader(context).ReadAsync(CancellationToken.None);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>().UseNpgsql(TestDatabase.ConnectionString).Options);
}
