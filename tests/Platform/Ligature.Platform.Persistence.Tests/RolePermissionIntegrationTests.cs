using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Roles.Commands.AddPermissionToRole;
using Ligature.Platform.Application.Roles.Commands.CreateRole;
using Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUT-C7/C8 end to end (docs/requirements.md, "AUT-C7 AddPermissionToRole and
/// AUT-C8 RemovePermissionFromRole", RG-A1 to RG-A16).
///
/// RP6 IS WHY THIS FILE IS LONG. The catalogue calls its rejection "the one
/// most likely to be omitted", and it cannot be exercised through the
/// application at all: the domain has no Agent factory (AU11), so every agent
/// here is staged in raw SQL, exactly as AuthorizationServiceTests stages its
/// own. Three cases prove the check tests ASSIGNMENT STATE and not merely
/// whether an agent exists.
///
/// The grant is closed, never deleted, and a re-grant is a NEW row (RP1) —
/// which is the whole reason role_permission has a surrogate key.
/// </summary>
public sealed class RolePermissionIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string SystemRole = "System roles cannot be modified.";

    private const string UnknownRole = "The role does not exist.";

    private const string UnknownPermission = "The permission does not exist.";

    private const string Inactive = "The permission is not active.";

    private const string AlreadyHeld = "The role already has this permission.";

    private const string UnknownGrant = "The role permission does not exist.";

    private const string AlreadyRevoked = "Role permission has already been revoked.";

    private const string HumanOnly =
        "This role is held by an agent, so it cannot be given a permission that requires a human actor. "
        + "Revoke the agent's assignment, add the permission, then grant the agent an agent-safe role.";

    private readonly ActivationDatabase _database;

    public RolePermissionIntegrationTests(ActivationDatabase database) => _database = database;

    // ---------------------------------------------------------------- RG-A1

    [Fact]
    public async Task A_permission_is_granted_to_a_tenant_role()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");

        var result = await AddAsync(admin, role.RoleId, permission);

        Assert.Equal(
            $"{role.RoleId.Value}|{permission.Value}||{admin.Value}",
            await GrantRowAsync(result.RolePermissionId));

        Assert.Equal(1, await LiveGrantCountAsync(role.RoleId));
    }

    // ---------------------------------------------------------------- RG-A2

    [Fact]
    public async Task The_grant_is_recorded_once_with_after_only_and_both_references()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");

        var result = await AddAsync(admin, role.RoleId, permission);

        var record = await RecordAsync(result.RolePermissionId.Value, "PermissionGrantedToRole");

        Assert.Equal(admin.Value, record[1]);
        Assert.Equal(DBNull.Value, record[2]);
        Assert.Equal(DBNull.Value, record[3]);
        Assert.Contains("user.read", (string)record[4]!);

        Assert.Equal(
            [("Granted", permission.Value.ToString()), ("Target", role.RoleId.Value.ToString())],
            await ReferencesAsync(result.RolePermissionId.Value));
    }

    // ---------------------------------------------------------------- RG-A3

    /// <summary>RP1: the revoked row stays, and a re-grant is a second row.</summary>
    [Fact]
    public async Task A_re_grant_after_a_revocation_is_a_second_row()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");

        var first = await AddAsync(admin, role.RoleId, permission);

        await RemoveAsync(admin, first.RolePermissionId, "Not needed after all.");

        var second = await AddAsync(admin, role.RoleId, permission);

        Assert.NotEqual(first.RolePermissionId, second.RolePermissionId);
        Assert.Equal(2, await GrantCountAsync(role.RoleId));
        Assert.Equal(1, await LiveGrantCountAsync(role.RoleId));
    }

    // ---------------------------------------------------------------- RG-A4

    [Fact]
    public async Task A_second_live_grant_for_the_pair_is_refused()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");

        await AddAsync(admin, role.RoleId, permission);

        Assert.Equal(AlreadyHeld, await RefusalAsync(() => AddAsync(admin, role.RoleId, permission)));
        Assert.Equal(1, await GrantCountAsync(role.RoleId));
    }

    /// <summary>RG6: the index is the guarantee, and it reads the same sentence.</summary>
    [Fact]
    public async Task A_race_past_the_pre_check_reads_the_same_sentence()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");

        // The pre-check passes: the row lands between it and the insert.
        await ExecuteAsync(
            $"""
             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by)
             VALUES ('{Guid.NewGuid()}', '{role.RoleId.Value}', '{permission.Value}', now(), '{User.SystemUserId.Value}');
             """);

        Assert.Equal(AlreadyHeld, await RefusalAsync(() => AddAsync(admin, role.RoleId, permission)));
    }

    // ---------------------------------------------------------------- RG-A5

    [Fact]
    public async Task An_unknown_permission_is_refused()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);

        Assert.Equal(
            UnknownPermission,
            await RefusalAsync(() => AddAsync(admin, role.RoleId, PermissionId.New())));

        Assert.Equal(0, await GrantCountAsync(role.RoleId));
    }

    [Fact]
    public async Task An_inactive_permission_is_refused()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("identity.read");

        await ExecuteAsync($"UPDATE permission SET is_active = false WHERE id = '{permission.Value}'");

        try
        {
            Assert.Equal(Inactive, await RefusalAsync(() => AddAsync(admin, role.RoleId, permission)));
            Assert.Equal(0, await GrantCountAsync(role.RoleId));
        }
        finally
        {
            await ExecuteAsync($"UPDATE permission SET is_active = true WHERE id = '{permission.Value}'");
        }
    }

    // ------------------------------------------------------ RG-A6: RP6

    /// <summary>
    /// The rejection the catalogue calls the one most likely to be omitted.
    /// The agent is staged in SQL because the domain refuses to create one.
    /// </summary>
    [Fact]
    public async Task A_human_only_permission_is_refused_while_an_agent_holds_the_role()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var humanOnly = await PermissionAsync("user.create");

        await SeedAgentHolderAsync(role.RoleId, revoked: false, ended: false);

        Assert.Equal(HumanOnly, await RefusalAsync(() => AddAsync(admin, role.RoleId, humanOnly)));
        Assert.Equal(0, await GrantCountAsync(role.RoleId));
    }

    /// <summary>The same role, a permission that any actor may exercise: accepted.</summary>
    [Fact]
    public async Task A_permission_any_actor_may_hold_is_accepted_while_an_agent_holds_the_role()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var anyActor = await PermissionAsync("user.read");

        await SeedAgentHolderAsync(role.RoleId, revoked: false, ended: false);

        await AddAsync(admin, role.RoleId, anyActor);

        Assert.Equal(1, await LiveGrantCountAsync(role.RoleId));
    }

    /// <summary>
    /// The case that proves the check reads ASSIGNMENT STATE, not the mere
    /// existence of an agent: a revoked or ended assignment does not block.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_human_only_permission_is_accepted_once_the_agents_assignment_is_over(bool revoked, bool ended)
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var humanOnly = await PermissionAsync("user.create");

        await SeedAgentHolderAsync(role.RoleId, revoked, ended);

        await AddAsync(admin, role.RoleId, humanOnly);

        Assert.Equal(1, await LiveGrantCountAsync(role.RoleId));
    }

    // ---------------------------------------------------------------- RG-A7

    [Fact]
    public async Task A_system_role_is_refused_by_both_commands()
    {
        var admin = await SecurityAdminAsync();
        var seeded = await SeededRoleAsync("access-reviewer");
        var permission = await PermissionAsync("session.read");
        var existing = await AnyLiveGrantAsync(seeded);

        Assert.Equal(SystemRole, await RefusalAsync(() => AddAsync(admin, seeded, permission)));
        Assert.Equal(SystemRole, await RefusalAsync(() => RemoveAsync(admin, existing, "Trying.")));

        Assert.Equal(DBNull.Value, await RevocationAsync(existing));
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var admin = await SecurityAdminAsync();

        var permission = await PermissionAsync("user.read");

        Assert.Equal(
            UnknownRole,
            await RefusalAsync(() => AddAsync(admin, RoleId.New(), permission)));
    }

    // ---------------------------------------------------------------- RG-A8

    [Fact]
    public async Task A_caller_without_role_manage_is_refused_by_both_commands()
    {
        var reviewer = await CallerAsync("aut-c7-access-reviewer", "access-reviewer");
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");
        var granted = await AddAsync(admin, role.RoleId, permission);

        var another = await PermissionAsync("session.read");

        Assert.Contains("permission",
            await RefusalAsync(() => AddAsync(reviewer, role.RoleId, another)),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("permission",
            await RefusalAsync(() => RemoveAsync(reviewer, granted.RolePermissionId, "Trying.")),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, await LiveGrantCountAsync(role.RoleId));
    }

    // ----------------------------------------------------- RG-A9 and RG-A10

    [Fact]
    public async Task A_grant_is_closed_and_not_deleted()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var permission = await PermissionAsync("user.read");
        var granted = await AddAsync(admin, role.RoleId, permission);

        await RemoveAsync(admin, granted.RolePermissionId, "Superseded.");

        Assert.Equal(1, await GrantCountAsync(role.RoleId));
        Assert.Equal(0, await LiveGrantCountAsync(role.RoleId));
        Assert.Equal(admin.Value, await RevokedByAsync(granted.RolePermissionId));

        var record = await RecordAsync(granted.RolePermissionId.Value, "PermissionRevokedFromRole");

        Assert.Equal(admin.Value, record[1]);
        Assert.Equal("Superseded.", record[2]);
        Assert.Contains("false", ((string)record[4]!).ToLowerInvariant());

        Assert.Equal(
            [("Revoked", permission.Value.ToString()), ("Target", role.RoleId.Value.ToString())],
            await ReferencesAsync(granted.RolePermissionId.Value));
    }

    // --------------------------------------------------------------- RG-A12

    [Fact]
    public async Task An_unknown_or_already_revoked_grant_is_refused()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var granted = await AddAsync(admin, role.RoleId, await PermissionAsync("user.read"));

        Assert.Equal(
            UnknownGrant,
            await RefusalAsync(() => RemoveAsync(admin, RolePermissionId.New(), "Gone.")));

        await RemoveAsync(admin, granted.RolePermissionId, "First.");

        Assert.Equal(
            AlreadyRevoked,
            await RefusalAsync(() => RemoveAsync(admin, granted.RolePermissionId, "Second.")));
    }

    // --------------------------------------------------------------- RG-A14

    /// <summary>The effect on authorisation is real, in both directions.</summary>
    [Fact]
    public async Task What_the_role_grants_reaches_its_holders_and_then_leaves_them()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);
        var holder = await SeedHumanHolderAsync(role.RoleId);
        var permission = await PermissionAsync("session.read");

        Assert.False(await AllowedAsync(holder, "session.read"));

        var granted = await AddAsync(admin, role.RoleId, permission);

        Assert.True(await AllowedAsync(holder, "session.read"));
        Assert.Contains("session.read", await EffectiveAsync(holder));

        await RemoveAsync(admin, granted.RolePermissionId, "No longer needed.");

        Assert.False(await AllowedAsync(holder, "session.read"));
        Assert.DoesNotContain("session.read", await EffectiveAsync(holder));
    }

    // --------------------------------------------------------------- RG-A15

    [Fact]
    public async Task The_derived_reads_follow_without_changing_their_definitions()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);

        var before = Assert.Single(await ListAsync(admin), x => x.RoleId == role.RoleId.Value);

        Assert.Equal(0, before.PermissionCount);
        Assert.True(before.AgentAssignable);

        var granted = await AddAsync(admin, role.RoleId, await PermissionAsync("user.create"));

        var withHumanOnly = Assert.Single(await ListAsync(admin), x => x.RoleId == role.RoleId.Value);

        Assert.Equal(1, withHumanOnly.PermissionCount);
        Assert.False(withHumanOnly.AgentAssignable);

        await RemoveAsync(admin, granted.RolePermissionId, "Reverting.");

        var after = Assert.Single(await ListAsync(admin), x => x.RoleId == role.RoleId.Value);

        Assert.Equal(0, after.PermissionCount);
        Assert.True(after.AgentAssignable);
    }

    // --------------------------------------------------------------- RG-A16

    [Fact]
    public async Task Nothing_but_the_grant_and_its_record_is_written()
    {
        var admin = await SecurityAdminAsync();
        var role = await CreateRoleAsync(admin);

        var before = await CountsAsync();

        await AddAsync(admin, role.RoleId, await PermissionAsync("user.read"));

        var after = await CountsAsync();

        Assert.Equal(before.Roles, after.Roles);
        Assert.Equal(before.Assignments, after.Assignments);
        Assert.Equal(before.Permissions, after.Permissions);
        Assert.Equal(before.Grants + 1, after.Grants);
    }

    // ================================================================ harness

    private Task<CreateRoleResult> CreateRoleAsync(UserId caller)
        => DispatchAsync<CreateRoleCommand, CreateRoleResult>(
            caller, new CreateRoleCommand($"tenant-{Guid.NewGuid():N}"[..24], "Quality Reviewer", null));

    private Task<AddPermissionToRoleResult> AddAsync(UserId caller, RoleId roleId, PermissionId permissionId)
        => DispatchAsync<AddPermissionToRoleCommand, AddPermissionToRoleResult>(
            caller, new AddPermissionToRoleCommand(roleId, permissionId));

    private Task<RemovePermissionFromRoleResult> RemoveAsync(UserId caller, RolePermissionId grantId, string reason)
        => DispatchAsync<RemovePermissionFromRoleCommand, RemovePermissionFromRoleResult>(
            caller, new RemovePermissionFromRoleCommand(grantId, reason));

    private async Task<bool> AllowedAsync(UserId user, string permission)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IAuthorizationService>()
            .IsAllowedAsync(new AuthorizationRequest(user, permission, DateTimeOffset.UtcNow, "Global", null),
                CancellationToken.None);

        return result.IsAllowed;
    }

    private async Task<IReadOnlyList<string>> EffectiveAsync(UserId user)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        var granted = await scope.ServiceProvider
            .GetRequiredService<IAuthorizationService>()
            .EnumerateAsync(new EffectivePermissionsRequest(user, DateTimeOffset.UtcNow), CancellationToken.None);

        return [.. granted.Select(x => x.Code)];
    }

    private async Task<IReadOnlyList<RoleSummary>> ListAsync(UserId caller)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<RoleAdministrationQuery, RoleAdministrationResult>(
                new RoleAdministrationQuery(true, false), CancellationToken.None);

        return result.Roles;
    }

    private async Task<TResult> DispatchAsync<TCommand, TResult>(UserId caller, TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<TCommand, TResult>(command, CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private static async Task<string> RefusalAsync(Func<Task> dispatch)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(dispatch);

        Assert.True(
            refusal is BusinessRuleViolationException or DomainException,
            $"Expected a refusal, got {refusal.GetType().Name}: {refusal.Message}");

        return refusal.Message;
    }

    private Task<UserId> SecurityAdminAsync() => CallerAsync("aut-c7-security-administrator", "security-administrator");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    private async Task<RoleId> SeededRoleAsync(string code)
        => new(await ScalarAsync<Guid>($"SELECT id FROM role WHERE code = '{code}'"));

    private async Task<PermissionId> PermissionAsync(string code)
        => new(await ScalarAsync<Guid>($"SELECT id FROM permission WHERE code = '{code}'"));

    private async Task<RolePermissionId> AnyLiveGrantAsync(RoleId roleId)
        => new(await ScalarAsync<Guid>(
            $"SELECT id FROM role_permission WHERE role_id = '{roleId.Value}' AND revoked_at IS NULL LIMIT 1"));

    /// <summary>
    /// An agent holding the role, in raw SQL: the domain has no Agent factory
    /// (AU11), and the composite FK (user_id, actor_type) refuses to let an
    /// existing user's kind change. Mirrors AuthorizationServiceTests.
    /// </summary>
    private async Task SeedAgentHolderAsync(RoleId roleId, bool revoked, bool ended)
    {
        var agent = Guid.NewGuid();
        var identity = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{agent}', 'Agent', NULL, NULL, 'Agent {agent:N}'::varchar, NULL,
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identity}', '{agent}', 'Agent', 'Local', 'Application',
                     '{identity}', 'agent-{identity:N}', 'Active', now(), '{system}');
             """);

        // UR8: an agent assignment must carry a finite end.
        var effectiveTo = ended ? "now() - interval '1 day'" : "now() + interval '30 days'";
        var effectiveFrom = ended ? "now() - interval '2 days'" : "now() - interval '1 day'";
        var revocation = revoked ? $"now(), '{system}', 'Ended for the test'" : "NULL, NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by,
                                    assignment_reason, revoked_at, revoked_by, revocation_reason,
                                    created_at, created_by)
             VALUES ('{Guid.NewGuid()}', '{agent}', 'Agent', '{roleId.Value}', 'Global', NULL,
                     {effectiveFrom}, {effectiveTo}, now(), '{system}',
                     'AUT-C7 RP6 fixture', {revocation}, now(), '{system}');
             """);
    }

    private async Task<UserId> SeedHumanHolderAsync(RoleId roleId)
    {
        var id = Guid.NewGuid();
        var identity = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Role', 'Holder', 'Role Holder', 'holder-{id:N}@example.test',
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identity}', '{id}', 'Human', 'Local', 'Application',
                     '{identity}', 'holder-{identity:N}', 'Active', now(), '{system}');

             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by,
                                    assignment_reason, created_at, created_by)
             VALUES ('{Guid.NewGuid()}', '{id}', 'Human', '{roleId.Value}', 'Global', NULL,
                     now() - interval '1 day', NULL, now(), '{system}',
                     'AUT-C7 holder fixture', now(), '{system}');
             """);

        return new UserId(id);
    }

    private Task<string> GrantRowAsync(RolePermissionId grantId)
        => ScalarAsync<string>(
            $"""
            SELECT concat_ws('|', role_id, permission_id, revoked_at, granted_by)
              FROM role_permission WHERE id = '{grantId.Value}'
            """);

    private Task<long> GrantCountAsync(RoleId roleId)
        => ScalarAsync<long>($"SELECT count(*) FROM role_permission WHERE role_id = '{roleId.Value}'");

    private Task<long> LiveGrantCountAsync(RoleId roleId)
        => ScalarAsync<long>(
            $"SELECT count(*) FROM role_permission WHERE role_id = '{roleId.Value}' AND revoked_at IS NULL");

    private async Task<object?> RevocationAsync(RolePermissionId grantId)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT revoked_at FROM role_permission WHERE id = '{grantId.Value}'", connection);

        return await command.ExecuteScalarAsync();
    }

    private Task<Guid> RevokedByAsync(RolePermissionId grantId)
        => ScalarAsync<Guid>($"SELECT revoked_by FROM role_permission WHERE id = '{grantId.Value}'");

    private async Task<List<(string Role, string Entity)>> ReferencesAsync(Guid entityId)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT r.entity_role, r.entity_id::text
              FROM audit.audit_entity_ref r
              JOIN audit.audit_record a ON a.audit_id = r.audit_id
             WHERE a.entity_id = '{entityId}'
             ORDER BY r.entity_role
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        var refs = new List<(string, string)>();

        while (await reader.ReadAsync())
            refs.Add((reader.GetString(0), reader.GetString(1)));

        return refs;
    }

    private async Task<object?[]> RecordAsync(Guid entityId, string eventType)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT event_type, actor_user_id, reason, before::text, after::text
              FROM audit.audit_record
             WHERE entity_id = '{entityId}' AND event_type = '{eventType}'
             ORDER BY sequence
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), $"no {eventType} record was written");

        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);

        return values;
    }

    private async Task<(long Roles, long Grants, long Assignments, long Permissions)> CountsAsync()
        => (await ScalarAsync<long>("SELECT count(*) FROM role"),
            await ScalarAsync<long>("SELECT count(*) FROM role_permission"),
            await ScalarAsync<long>("SELECT count(*) FROM user_role"),
            await ScalarAsync<long>("SELECT count(*) FROM permission"));

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
