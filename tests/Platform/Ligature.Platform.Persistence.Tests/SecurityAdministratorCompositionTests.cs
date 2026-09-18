using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C1 Amendment 1, S2 and S5 (docs/requirements.md): what a freshly
/// provisioned tenant stores, and what that lets a security administrator do.
///
/// Its own provisioned database. The shared one was provisioned by an earlier
/// release and reaches this composition only through catalogue
/// synchronisation, which is a different claim (S3).
/// </summary>
public sealed class SecurityAdministratorCompositionTests : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public SecurityAdministratorCompositionTests(ActivationDatabase database) => _database = database;

    /// <summary>S2 — stored by PRV-C1 itself, attributed to the System actor.</summary>
    [Fact]
    public async Task A_new_tenant_grants_user_read_to_security_administrator()
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT rp.granted_by
              FROM role_permission rp
              JOIN role r       ON r.id = rp.role_id
              JOIN permission p ON p.id = rp.permission_id
             WHERE r.code = 'security-administrator'
               AND p.code = 'user.read'
               AND rp.revoked_at IS NULL
            """, connection);

        var grantedBy = await command.ExecuteScalarAsync();

        Assert.Equal(User.SystemUserId.Value, Assert.IsType<Guid>(grantedBy));
    }

    /// <summary>
    /// S5 — through the real pipeline and the real authorization service, so
    /// the grant is proved to be one the pipeline honours, not only a row.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_only_security_administrator_may_list_users()
    {
        var caller = await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, "prv-c1-security-administrator", "security-administrator");

        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller.UserId, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UsersQuery, UsersResult>(new UsersQuery(null, null), CancellationToken.None);

        Assert.Contains(result.Users, x => x.UserId == caller.UserId);
    }
}
