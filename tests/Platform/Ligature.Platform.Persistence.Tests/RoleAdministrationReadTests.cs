using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Application.Roles.Queries.RolePermissions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The role administration reads (docs/requirements.md, "Role administration
/// read", RA-A1 to RA-A11), against PostgreSQL.
///
/// THE DERIVED VALUES ARE THE POINT. permissionCount, activeHolderCount and
/// agentAssignable are each defined by a different rule (RA2, RA3, RA4), and
/// every row below exists to hold one of them to it: a revoked grant, a grant
/// of a retired permission, assignments in each of the four states, a holder
/// whose user is inactive, and a role with nothing granted at all.
///
/// Its own provisioned database: these tests create roles, which the shared
/// database's catalogue-drift checks would refuse.
/// </summary>
public sealed class RoleAdministrationReadTests : IClassFixture<ActivationDatabase>
{
    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ActivationDatabase _database;

    public RoleAdministrationReadTests(ActivationDatabase database) => _database = database;

    // =============================================================== AUT-Q5

    /// <summary>RA-A1: exactly the nine members, the stored values, ordered by name.</summary>
    [Fact]
    public async Task A_role_is_answered_with_exactly_its_nine_members()
    {
        var reader = await ReaderAsync();
        var code = await SeedRoleAsync(name: "Zzz Administration", description: "For the read tests.");

        var roles = await ListAsync(reader);
        var role = Single(roles, code);

        Assert.Equal(code, role.Code);
        Assert.Equal("Zzz Administration", role.Name);
        Assert.Equal("For the read tests.", role.Description);
        Assert.False(role.IsSystemRole);
        Assert.True(role.IsActive);
        Assert.Equal(0, role.PermissionCount);
        Assert.Equal(0, role.ActiveHolderCount);
        Assert.True(role.AgentAssignable);

        var names = roles.Select(x => x.Name).ToList();
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), names);
    }

    /// <summary>RA-A2: the parameter narrows the result set and nothing else.</summary>
    [Fact]
    public async Task IncludeInactive_changes_the_result_set_only()
    {
        var reader = await ReaderAsync();
        var active = await SeedRoleAsync();
        var inactive = await SeedRoleAsync(isActive: false);

        await GrantAsync(inactive, "user.read", Past);

        var withoutInactive = await ListAsync(reader);
        var withInactive = await ListAsync(reader, includeInactive: true);

        Assert.DoesNotContain(withoutInactive, x => x.Code == inactive);
        Assert.Contains(withoutInactive, x => x.Code == active);

        var row = Single(withInactive, inactive);

        Assert.False(row.IsActive);
        Assert.Equal(1, row.PermissionCount);
        Assert.True(row.AgentAssignable);

        // The same role's derived values, read both ways, are identical.
        Assert.Equal(
            Single(withInactive, active),
            Single(withoutInactive, active));
    }

    /// <summary>RA-A3: assignment state decides, not the holder's status.</summary>
    [Fact]
    public async Task ActiveHolderCount_counts_active_assignments_only()
    {
        var reader = await ReaderAsync();
        var code = await SeedRoleAsync();
        var roleId = await RoleIdAsync(code);

        var held = await SeedUserAsync();
        var future = await SeedUserAsync();
        var ended = await SeedUserAsync();
        var revoked = await SeedUserAsync();
        var departed = await SeedUserAsync(inactive: true);

        await AssignAsync(held, roleId, from: Past, to: null);
        await AssignAsync(future, roleId, from: DateTimeOffset.UtcNow.AddDays(10), to: null);
        await AssignAsync(ended, roleId, from: Past, to: Past.AddDays(1));
        await AssignAsync(revoked, roleId, from: Past, to: null, revoked: true);
        await AssignAsync(departed, roleId, from: Past, to: null);

        // A second, scoped assignment for someone already counted: a HOLDER
        // count, not an assignment count.
        await AssignAsync(held, roleId, from: Past, to: null, scopeId: Guid.NewGuid());

        Assert.Equal(2, Single(await ListAsync(reader), code).ActiveHolderCount);
    }

    /// <summary>RA-A4: live grants, whatever the catalogue says about the permission.</summary>
    [Fact]
    public async Task PermissionCount_counts_live_grants_including_retired_permissions()
    {
        var reader = await ReaderAsync();
        var code = await SeedRoleAsync();
        var retired = await SeedPermissionAsync(requiresHumanActor: false, isActive: false);

        await GrantAsync(code, "user.read", Past);
        await GrantAsync(code, retired, Past);
        await GrantAsync(code, "session.read", Past, revokedAt: Past.AddDays(1));

        Assert.Equal(2, Single(await ListAsync(reader), code).PermissionCount);
    }

    /// <summary>RA-A5: the frozen derivation, and the filter that uses it.</summary>
    [Fact]
    public async Task AgentAssignable_is_derived_from_live_human_only_grants()
    {
        var reader = await ReaderAsync();

        var empty = await SeedRoleAsync();
        var agentSafe = await SeedRoleAsync();
        var humanOnly = await SeedRoleAsync();
        var wasHumanOnly = await SeedRoleAsync();

        await GrantAsync(agentSafe, "user.read", Past);
        await GrantAsync(humanOnly, "user.create", Past);
        await GrantAsync(wasHumanOnly, "user.create", Past, revokedAt: Past.AddDays(1));

        var roles = await ListAsync(reader);

        Assert.True(Single(roles, empty).AgentAssignable);
        Assert.True(Single(roles, agentSafe).AgentAssignable);
        Assert.False(Single(roles, humanOnly).AgentAssignable);
        Assert.True(Single(roles, wasHumanOnly).AgentAssignable);

        var filtered = await ListAsync(reader, agentAssignableOnly: true);

        Assert.All(filtered, x => Assert.True(x.AgentAssignable));
        Assert.Contains(filtered, x => x.Code == empty);
        Assert.DoesNotContain(filtered, x => x.Code == humanOnly);
    }

    [Fact]
    public async Task AgentAssignableOnly_combines_with_includeInactive()
    {
        var reader = await ReaderAsync();
        var inactiveSafe = await SeedRoleAsync(isActive: false);

        await GrantAsync(inactiveSafe, "user.read", Past);

        Assert.DoesNotContain(await ListAsync(reader, agentAssignableOnly: true), x => x.Code == inactiveSafe);
        Assert.Contains(
            await ListAsync(reader, includeInactive: true, agentAssignableOnly: true),
            x => x.Code == inactiveSafe);
    }

    // =============================================================== AUT-Q3

    /// <summary>RA-A6: the current live grants, exactly nine members, ordered by code.</summary>
    [Fact]
    public async Task A_roles_current_permissions_are_answered_in_code_order()
    {
        var reader = await ReaderAsync();
        var code = await SeedRoleAsync();

        await GrantAsync(code, "user.read", Past);
        await GrantAsync(code, "user.create", Past);
        await GrantAsync(code, "session.read", Past, revokedAt: Past.AddDays(1));

        var grants = await PermissionsAsync(reader, await RoleIdAsync(code));

        Assert.Equal(["user.create", "user.read"], grants.Select(x => x.Code));

        var created = grants.Single(x => x.Code == "user.create");

        Assert.NotEqual(Guid.Empty, created.RolePermissionId);
        Assert.NotEqual(Guid.Empty, created.PermissionId);
        Assert.Equal("user", created.Resource);
        Assert.Equal("create", created.Action);
        Assert.True(created.RequiresHumanActor);
        Assert.Equal(Past, created.GrantedAt);
        Assert.Null(created.RevokedAt);
    }

    /// <summary>RA-A7: live AT THE INSTANT — revoked later shows, granted later does not.</summary>
    [Fact]
    public async Task AsOf_answers_the_grants_live_at_that_instant()
    {
        var reader = await ReaderAsync();
        var code = await SeedRoleAsync();
        var roleId = await RoleIdAsync(code);
        var asOf = Past.AddDays(10);

        await GrantAsync(code, "user.read", Past);                                   // still live
        await GrantAsync(code, "user.create", Past, revokedAt: asOf.AddDays(1));     // revoked after
        await GrantAsync(code, "session.read", asOf.AddDays(1));                     // granted after
        await GrantAsync(code, "session.revoke", Past, revokedAt: asOf.AddDays(-1)); // revoked before

        var grants = await PermissionsAsync(reader, roleId, asOf);

        Assert.Equal(["user.create", "user.read"], grants.Select(x => x.Code));
        Assert.Equal(asOf.AddDays(1), grants.Single(x => x.Code == "user.create").RevokedAt);
    }

    /// <summary>RA-A8 and RA11: an empty list means the role has no grants; an unknown role does not exist.</summary>
    [Fact]
    public async Task An_empty_role_and_an_unknown_role_are_different_answers()
    {
        var reader = await ReaderAsync();
        var empty = await RoleIdAsync(await SeedRoleAsync());

        var granted = await QueryAsync<RolePermissionsQuery, RolePermissionsResult>(
            reader, new RolePermissionsQuery(empty, null));

        Assert.True(granted.RoleExists);
        Assert.Empty(granted.Permissions!);

        var unknown = await QueryAsync<RolePermissionsQuery, RolePermissionsResult>(
            reader, new RolePermissionsQuery(RoleId.New(), null));

        Assert.False(unknown.RoleExists);
    }

    // =============================================================== AUT-Q6

    /// <summary>RA-A9: the catalogue, exactly seven members, in code order, retired entries included.</summary>
    [Fact]
    public async Task The_permission_catalogue_is_answered_in_code_order()
    {
        var reader = await ReaderAsync();
        var retired = await SeedPermissionAsync(requiresHumanActor: false, isActive: false);

        var permissions = await CatalogueAsync(reader);
        var codes = permissions.Select(x => x.Code).ToList();

        Assert.Equal(codes.OrderBy(x => x, StringComparer.Ordinal), codes);
        Assert.Contains(permissions, x => x.Code == retired && !x.IsActive);

        var create = permissions.Single(x => x.Code == "user.create");

        Assert.NotEqual(Guid.Empty, create.PermissionId);
        Assert.Equal("user", create.Resource);
        Assert.Equal("create", create.Action);
        Assert.True(create.RequiresHumanActor);
        Assert.True(create.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(create.Name));
    }

    /// <summary>RA-A10: the two filters, separately and together.</summary>
    [Fact]
    public async Task The_catalogue_filters_by_resource_and_by_human_only()
    {
        var reader = await ReaderAsync();

        var byResource = await CatalogueAsync(reader, resource: "session");
        Assert.NotEmpty(byResource);
        Assert.All(byResource, x => Assert.Equal("session", x.Resource));

        var humanOnly = await CatalogueAsync(reader, requiresHumanActor: true);
        Assert.NotEmpty(humanOnly);
        Assert.All(humanOnly, x => Assert.True(x.RequiresHumanActor));

        var both = await CatalogueAsync(reader, resource: "user", requiresHumanActor: false);
        Assert.NotEmpty(both);
        Assert.All(both, x =>
        {
            Assert.Equal("user", x.Resource);
            Assert.False(x.RequiresHumanActor);
        });
    }

    // ============================================================== RA-A11

    [Fact]
    public async Task Every_read_requires_role_read_and_a_caller_and_writes_no_record()
    {
        var reader = await ReaderAsync();
        var without = await CallerAsync("aut-q5-user-administrator", "user-administrator");
        var records = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record");

        await ListAsync(reader);
        await PermissionsAsync(reader, await RoleIdAsync(await SeedRoleAsync()));
        await CatalogueAsync(reader);

        Assert.Equal(records, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record"));

        foreach (var refused in new Func<Task>[]
        {
            () => ListAsync(without),
            async () => await PermissionsAsync(without, RoleId.New()),
            () => CatalogueAsync(without),
        })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(refused);
            Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => AnonymousAsync<RoleAdministrationQuery, RoleAdministrationResult>(new RoleAdministrationQuery(false, false)));
    }

    // ============================================================== harness

    private static RoleSummary Single(IReadOnlyList<RoleSummary> roles, string code)
        => roles.Single(x => x.Code == code);

    private async Task<IReadOnlyList<RoleSummary>> ListAsync(
        UserId caller, bool includeInactive = false, bool agentAssignableOnly = false)
        => (await QueryAsync<RoleAdministrationQuery, RoleAdministrationResult>(
            caller, new RoleAdministrationQuery(includeInactive, agentAssignableOnly))).Roles;

    private async Task<IReadOnlyList<RolePermissionGrant>> PermissionsAsync(
        UserId caller, RoleId roleId, DateTimeOffset? asOf = null)
        => (await QueryAsync<RolePermissionsQuery, RolePermissionsResult>(
            caller, new RolePermissionsQuery(roleId, asOf))).Permissions
            ?? throw new InvalidOperationException("The role does not exist.");

    private async Task<IReadOnlyList<PermissionCatalogueEntry>> CatalogueAsync(
        UserId caller, string? resource = null, bool? requiresHumanActor = null)
        => (await QueryAsync<PermissionCatalogueQuery, PermissionCatalogueResult>(
            caller, new PermissionCatalogueQuery(resource, requiresHumanActor))).Permissions;

    private async Task<TResult> QueryAsync<TQuery, TResult>(UserId caller, TQuery query)
        where TQuery : IQuery<TResult>
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<TQuery, TResult>(query, CancellationToken.None);
    }

    private async Task<TResult> AnonymousAsync<TQuery, TResult>(TQuery query)
        where TQuery : IQuery<TResult>
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<TQuery, TResult>(query, CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private Task<UserId> ReaderAsync() => CallerAsync("aut-q5-access-reviewer", "access-reviewer");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    /// <summary>A tenant role, active unless said otherwise. Returns its code.</summary>
    private async Task<string> SeedRoleAsync(bool isActive = true, string? name = null, string? description = null)
    {
        var id = Guid.NewGuid();
        var code = $"tenant-{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', @name, '{code}', @description, false, {(isActive ? "true" : "false")},
                     now(), '{system}', now(), '{system}');
             """,
            ("name", (object?)(name ?? $"Tenant role {code}") ?? DBNull.Value),
            ("description", (object?)description ?? DBNull.Value));

        return code;
    }

    /// <summary>A catalogue entry of this test's own. Returns its code.</summary>
    private async Task<string> SeedPermissionAsync(bool requiresHumanActor, bool isActive)
    {
        var id = Guid.NewGuid();
        var code = $"zz-test.{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO permission (id, code, name, description, resource, action,
                                     requires_human_actor, is_active, created_at, created_by)
             VALUES ('{id}', '{code}', 'Seeded {code}', NULL, 'zz-test', 'read',
                     {(requiresHumanActor ? "true" : "false")}, {(isActive ? "true" : "false")},
                     now(), '{system}');
             """);

        return code;
    }

    private async Task GrantAsync(
        string roleCode, string permissionCode, DateTimeOffset grantedAt, DateTimeOffset? revokedAt = null)
    {
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by, revoked_at, revoked_by)
             SELECT '{Guid.NewGuid()}', r.id, p.id, @granted, '{system}', @revoked,
                    {(revokedAt is null ? "NULL" : $"'{system}'")}
               FROM role r, permission p
              WHERE r.code = '{roleCode}' AND p.code = '{permissionCode}';
             """,
            ("granted", grantedAt),
            ("revoked", (object?)revokedAt ?? DBNull.Value));
    }

    private async Task<UserId> SeedUserAsync(bool inactive = false)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Role', 'Holder', 'Role Holder',
                     'holder-{id:N}@example.test', {status}, now(), '{system}', now(), '{system}');
             """);

        return new UserId(id);
    }

    private async Task AssignAsync(
        UserId user, RoleId role, DateTimeOffset from, DateTimeOffset? to, bool revoked = false, Guid? scopeId = null)
    {
        var system = User.SystemUserId.Value;
        var scope = scopeId is null ? ("'Global'", "NULL") : ("'Project'", $"'{scopeId}'");

        await ExecuteAsync(
            $"""
             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                                    revoked_at, revoked_by, revocation_reason)
             VALUES ('{Guid.NewGuid()}', '{user.Value}', 'Human', '{role.Value}', {scope.Item1}, {scope.Item2},
                     @from, @to, now(), '{system}', 'Seeded for the role administration reads.',
                     {(revoked ? "now()" : "NULL")}, {(revoked ? $"'{system}'" : "NULL")},
                     {(revoked ? "'Seeded revoked.'" : "NULL")});
             """,
            ("from", from),
            ("to", (object?)to ?? DBNull.Value));
    }

    private async Task<RoleId> RoleIdAsync(string code)
        => new(await ScalarAsync<Guid>($"SELECT id FROM role WHERE code = '{code}'"));

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }
}
