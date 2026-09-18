using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Application.Users.Queries.RoleAssignments;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUT-Q2 end to end: the real commands grant and revoke, and the real query
/// is dispatched through the pipeline at chosen instants (docs/requirements.md,
/// "AUT-Q2", Q3). The state is the server's, derived at the instant of the
/// read, and these follow one assignment through its life:
///
///   Future -> Active -> Ended      a grant with an end
///   Future -> Revoked              a future grant cancelled before it starts,
///                                  Revoked at every instant, including after
///                                  its (empty) period
///
/// Its own provisioned database: the administrator becomes the actor of audit
/// records.
/// </summary>
public sealed class RoleAssignmentLifecycleTests : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public RoleAssignmentLifecycleTests(ActivationDatabase database) => _database = database;

    [Fact]
    public async Task A_grant_with_an_end_reads_future_then_active_then_ended()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(10));
        var to = from.AddDays(30);

        var granted = await GrantAsync(admin, target, from, to);

        Assert.Equal(RoleAssignmentState.Future, await StateAtAsync(admin, target, granted, from.AddDays(-1)));
        Assert.Equal(RoleAssignmentState.Active, await StateAtAsync(admin, target, granted, from.AddDays(1)));
        Assert.Equal(RoleAssignmentState.Ended, await StateAtAsync(admin, target, granted, to.AddDays(1)));
    }

    [Fact]
    public async Task A_future_grant_revoked_before_it_starts_reads_revoked_at_every_instant()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(10));

        var granted = await GrantAsync(admin, target, from, null);

        Assert.Equal(RoleAssignmentState.Future, await StateAtAsync(admin, target, granted, DateTimeOffset.UtcNow));

        await RevokeAsync(admin, granted);

        foreach (var at in new[] { DateTimeOffset.UtcNow, from, from.AddDays(1), from.AddYears(1) })
            Assert.Equal(RoleAssignmentState.Revoked, await StateAtAsync(admin, target, granted, at));

        var row = Assert.Single((await ReadAsync(admin, target, DateTimeOffset.UtcNow, includeInactive: true)).Assignments);

        // The empty period, as stored and as served.
        Assert.Equal(from, row.EffectiveFrom);
        Assert.Equal(from, row.EffectiveTo);
        Assert.Equal(admin, row.RevokedBy!.UserId);
    }

    /// <summary>History is opt-in: a revoked assignment is only in the full read.</summary>
    [Fact]
    public async Task A_revoked_assignment_appears_only_with_history()
    {
        var admin = await AdminAsync();
        var target = await SeedTargetAsync();

        var granted = await GrantAsync(admin, target, null, null);
        await RevokeAsync(admin, granted);

        Assert.Empty((await ReadAsync(admin, target, DateTimeOffset.UtcNow, includeInactive: false)).Assignments);
        Assert.Single((await ReadAsync(admin, target, DateTimeOffset.UtcNow, includeInactive: true)).Assignments);
    }

    // ================================================================ harness

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero);

    private async Task<RoleAssignmentState> StateAtAsync(UserId admin, UserId target, UserRoleId assignment, DateTimeOffset at)
        => Assert.Single(
                (await ReadAsync(admin, target, at, includeInactive: true)).Assignments,
                x => x.AssignmentId == assignment)
            .State;

    private async Task<UserRoleAssignmentsResult> ReadAsync(UserId caller, UserId target, DateTimeOffset at, bool includeInactive)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        // Only the read's clock moves; the assignment was written at real time.
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FixedClock(at));

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Establish(scope, caller);

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserRoleAssignmentsQuery, UserRoleAssignmentsResult>(
                new UserRoleAssignmentsQuery(target, includeInactive), CancellationToken.None);
    }

    private async Task<UserRoleId> GrantAsync(UserId caller, UserId target, DateTimeOffset? from, DateTimeOffset? to)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller);

        return (await scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>()
                .SendAsync<GrantRoleCommand, GrantRoleResult>(
                    new GrantRoleCommand(target, await RoleIdAsync("access-reviewer"), from, to, "Lifecycle test grant."),
                    CancellationToken.None))
            .UserRoleAssignmentId;
    }

    private async Task RevokeAsync(UserId caller, UserRoleId assignment)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller);

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<RevokeRoleCommand, RevokeRoleResult>(
                new RevokeRoleCommand(assignment, "Lifecycle test revocation."), CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private static void Establish(IServiceScope scope, UserId caller)
        => scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

    private async Task<UserId> AdminAsync()
        => (await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "aut-q2-security-administrator", "security-administrator")).UserId;

    private async Task<UserId> SeedTargetAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'Life', 'Cycle', 'Life Cycle {unique[..8]}', 'lifecycle-{unique}@example.test',
                     'Active', now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'lifecycle-{unique[..16]}', 'Active', now() - interval '1 day', '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return new UserId(userId);
    }

    private async Task<RoleId> RoleIdAsync(string code)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM role WHERE code = @code", connection);

        command.Parameters.AddWithValue("code", code);

        return new RoleId((Guid)(await command.ExecuteScalarAsync())!);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
