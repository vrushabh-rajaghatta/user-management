using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The database is the enforcer; the pre-checks are affordances. These tests
/// cover the path the pre-check cannot close — two callers both observe
/// "available", and the loser's 23505 has to arrive as a domain error rather
/// than a Postgres error string.
///
/// Equally important is what is NOT translated. The translator is deliberately
/// narrow, so a violation it does not recognise must surface unchanged: an
/// expected race, a programming error and a broken invariant are different
/// things, and a generic fallback would flatten them together.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class ExceptionTranslationTests
{

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 13, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------- 1/2. translated

    [Fact]
    public async Task A_duplicate_active_human_email_becomes_a_business_rule_error()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var first = NewHuman();
        var duplicate = NewHuman(email: first.Email!.Value.ToUpperInvariant());

        try
        {
            context.Add(first);
            await context.SaveChangesAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        context.Add(duplicate);

                        return Task.FromResult(duplicate.Id);
                    },
                    CancellationToken.None));

            Assert.Equal(
                "A user with this email address already exists.",
                failure.Message);
        }
        finally
        {
            await CleanUpAsync(first.Id);
            await CleanUpAsync(duplicate.Id);
        }
    }

    [Fact]
    public async Task A_duplicate_local_username_becomes_a_business_rule_error()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var first = NewHuman();
        var second = NewHuman();
        var username = $"dup-{Guid.NewGuid():N}"[..20];

        try
        {
            context.Add(first);
            context.Add(LocalIdentityFor(first, username));
            await context.SaveChangesAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        context.Add(second);
                        context.Add(
                            LocalIdentityFor(second, username.ToUpperInvariant()));

                        return Task.FromResult(second.Id);
                    },
                    CancellationToken.None));

            Assert.Equal(
                "A user identity with this username already exists.",
                failure.Message);
        }
        finally
        {
            await CleanUpAsync(first.Id);
            await CleanUpAsync(second.Id);
        }
    }

    // --------------------------------------------------- 3/4. NOT translated

    /// <summary>
    /// A unique violation on a constraint the translator does not know must
    /// arrive intact.
    ///
    /// THIS TEST'S EXAMPLE KEEPS MOVING, AND THAT IS THE POINT. It was
    /// IX_role_code until AUT-C3 made that reachable, then RP2's live-grant
    /// index until AUT-C7 made THAT reachable. Each time the constraint was
    /// mapped by the story whose command could finally reach it, and this test
    /// moved to one still out of reach — a wording written blind is a wording
    /// nothing tests.
    ///
    /// It is now the app_user primary key, reached by asking for a second
    /// System actor: User.CreateSystem always uses the one fixed id, so the
    /// insert collides. No command creates a user with an id it did not
    /// generate, so nothing but a deliberate act like this one raises it.
    /// </summary>
    [Fact]
    public async Task An_unknown_unique_violation_survives_untranslated()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.ExecuteInTransactionAsync(
                _ =>
                {
                    context.Add(User.CreateSystem(Now));

                    return Task.FromResult(User.SystemUserId);
                },
                CancellationToken.None));

        var postgres = Assert.IsType<PostgresException>(failure.InnerException);

        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("PK_app_user", postgres.ConstraintName);
    }

    /// <summary>
    /// AUT-C3 (RC5): the loser of a race on a role code reads the command's own
    /// sentence, not a Postgres error string — the same sentence the
    /// case-insensitive pre-check gives.
    /// </summary>
    [Fact]
    public async Task A_duplicate_role_code_becomes_a_business_rule_error()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var code = $"dup-role-{Guid.NewGuid():N}"[..24];
        var first = NewRole(code);
        var duplicate = NewRole(code);

        try
        {
            context.Add(first);
            await context.SaveChangesAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        context.Add(duplicate);

                        return Task.FromResult(duplicate.Id);
                    },
                    CancellationToken.None));

            Assert.Equal("A role with this code already exists.", failure.Message);
        }
        finally
        {
            await DeleteRoleAsync(first.Id);
            await DeleteRoleAsync(duplicate.Id);
        }
    }

    private static async Task<PermissionId> FirstPermissionIdAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT id FROM permission ORDER BY code LIMIT 1", connection);

        return new PermissionId((Guid)(await command.ExecuteScalarAsync())!);
    }

    private static async Task DeleteGrantsAsync(RoleId roleId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("DELETE FROM role_permission WHERE role_id = @id", connection);
        command.Parameters.AddWithValue("id", roleId.Value);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A non-unique violation — here a check constraint, meaning the code built
    /// a row the model forbids. That is a bug, and it must not be dressed as
    /// user error, or someone goes looking at their own input instead of at the
    /// stack trace.
    /// </summary>
    [Fact]
    public async Task A_check_constraint_violation_survives_untranslated()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewHuman();

        try
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        context.Add(user);

                        // ck_app_user_human_names: a Human must carry names.
                        context.Entry(user).Property(x => x.FirstName)
                            .CurrentValue = null;

                        return Task.FromResult(user.Id);
                    },
                    CancellationToken.None));

            var postgres = Assert.IsType<PostgresException>(failure.InnerException);

            Assert.Equal("23514", postgres.SqlState);
            Assert.Equal("ck_app_user_human_names", postgres.ConstraintName);
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// An exclusion-constraint violation (23P01), from UR5's no-overlap rule,
    /// becomes the ordinary refusal AUT-C1 defines (docs/requirements.md,
    /// "Role Assignment").
    ///
    /// It is also the case that proves dispatch is by constraint NAME rather
    /// than by SqlState: this error is not 23505, so a unique-violation-only
    /// guard would leave this mapping silently dead.
    /// </summary>
    [Fact]
    public async Task An_overlapping_global_assignment_becomes_the_overlap_refusal()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewHuman();
        var role = NewRole($"overlap-{Guid.NewGuid():N}"[..24]);

        try
        {
            context.Add(user);
            context.Add(role);
            context.Add(AssignmentFor(user, role));
            await context.SaveChangesAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        // Same user, role and scope, overlapping period.
                        context.Add(AssignmentFor(user, role));

                        return Task.FromResult(user.Id);
                    },
                    CancellationToken.None));

            Assert.Equal(OverlapRefusal, failure.Message);
        }
        finally
        {
            await DeleteAssignmentsAsync(user.Id);
            await CleanUpAsync(user.Id);
            await DeleteRoleAsync(role.Id);
        }
    }

    /// <summary>
    /// UR6, the scoped twin. No command writes a scoped assignment in v1, but
    /// the constraint exists and the same question has the same answer, so it
    /// is mapped too rather than surfacing as a 500 the day scopes arrive.
    /// </summary>
    [Fact]
    public async Task An_overlapping_scoped_assignment_becomes_the_overlap_refusal()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var user = NewHuman();
        var role = NewRole($"overlap-{Guid.NewGuid():N}"[..24]);
        var scopeId = Guid.NewGuid();

        try
        {
            context.Add(user);
            context.Add(role);
            context.Add(AssignmentFor(user, role, scopeId));
            await context.SaveChangesAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        context.Add(AssignmentFor(user, role, scopeId));

                        return Task.FromResult(user.Id);
                    },
                    CancellationToken.None));

            Assert.Equal(OverlapRefusal, failure.Message);
        }
        finally
        {
            await DeleteAssignmentsAsync(user.Id);
            await CleanUpAsync(user.Id);
            await DeleteRoleAsync(role.Id);
        }
    }

    // ------------------------------------------------- 6. transaction intact

    /// <summary>
    /// Translation changes which exception surfaces, never whether one does —
    /// so the commit is still unreachable and the transaction still rolls back.
    /// If translation ever swallowed the failure, the partial write would
    /// commit and this would find a row.
    /// </summary>
    [Fact]
    public async Task A_translated_failure_still_rolls_the_transaction_back()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var first = NewHuman();
        var duplicate = NewHuman(email: first.Email!.Value);
        var bystander = NewHuman();

        try
        {
            context.Add(first);
            await context.SaveChangesAsync(CancellationToken.None);

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        // Written in the same unit as the doomed row.
                        context.Add(bystander);
                        context.Add(duplicate);

                        return Task.FromResult(duplicate.Id);
                    },
                    CancellationToken.None));

            Assert.Equal(0, await CountUsersAsync(bystander.Id));
            Assert.Equal(0, await CountUsersAsync(duplicate.Id));
            Assert.Equal(1, await CountUsersAsync(first.Id));
        }
        finally
        {
            await CleanUpAsync(first.Id);
            await CleanUpAsync(duplicate.Id);
            await CleanUpAsync(bystander.Id);
        }
    }

    // ------------------------------------------------------------- fixtures

    private static User NewHuman(string? email = null)
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return User.CreateHuman(
            UserId.New(),
            "Trans",
            "Lation",
            $"Trans Lation {discriminator[..8]}",
            email ?? $"Trans-{discriminator}@Example.Test",
            Now,
            User.SystemUserId);
    }

    private static UserIdentity LocalIdentityFor(User user, string username)
        => UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            username,
            Now,
            User.SystemUserId);

    private static Role NewRole(string code)
        => Role.Create(
            RoleId.New(),
            $"Translation Test {Guid.NewGuid().ToString("N")[..8]}",
            code,
            "Created by ExceptionTranslationTests.",
            isSystemRole: false,
            Now,
            User.SystemUserId);

    private const string OverlapRefusal =
        "The user already holds this role for this scope in an overlapping period.";

    private static UserRole AssignmentFor(User user, Role role, Guid? scopeId = null)
        => UserRole.Create(
            UserRoleId.New(),
            user.Id,
            ActorType.Human,
            role.Id,
            scopeId is null ? ScopeType.Global : ScopeType.Create("Product"),
            scopeId: scopeId,
            effectiveFrom: Now,
            effectiveTo: null,
            assignedAt: Now,
            assignedBy: User.SystemUserId,
            assignmentReason: "Exception translation tests.",
            createdAt: Now,
            createdBy: User.SystemUserId);

    private static async Task DeleteAssignmentsAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM user_role WHERE user_id = @id", connection);

        command.Parameters.AddWithValue("id", userId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsProvisionedAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE actor_type = 'System'",
            connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
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

    private static async Task<int> CountUsersAsync(UserId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task CleanUpAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task DeleteRoleAsync(RoleId roleId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM role WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", roleId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionString => TestDatabase.ConnectionString;


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
