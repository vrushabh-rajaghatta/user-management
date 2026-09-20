using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Roles.Commands.CreateRole;
using Ligature.Platform.Application.Roles.Commands.DeactivateRole;
using Ligature.Platform.Application.Roles.Commands.ReactivateRole;
using Ligature.Platform.Application.Roles.Commands.UpdateRoleMetadata;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Queries.GrantableRoles;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUT-C5/C6 end to end (docs/requirements.md, "AUT-C5 DeactivateRole and
/// AUT-C6 ReactivateRole", RD-A1 to RD-A11).
///
/// THE POINT OF THE STORY IS A DISTINCTION, and these tests are arranged
/// around it:
///
///     ELIGIBILITY changes  — AUT-C1 refuses the role, the grant form drops
///                            it, AUT-Q5 hides it without includeInactive.
///     AUTHORIZATION does not — every existing holder keeps working, on both
///                            views, and no assignment row moves.
///
/// AuthorizationServiceTests already pins the second property against the
/// COLUMN. These pin it against the COMMAND, which is the new thing: a
/// handler that reached for role.IsActive, or cascaded into user_role, would
/// pass there and fail here.
/// </summary>
public sealed class RoleLifecycleIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string NoDeactivate = "System roles cannot be deactivated.";

    private const string NoReactivate = "System roles cannot be reactivated.";

    private const string Unknown = "The role does not exist.";

    private const string NotGrantable = "This role cannot be granted.";

    private readonly ActivationDatabase _database;

    public RoleLifecycleIntegrationTests(ActivationDatabase database) => _database = database;

    // ---------------------------------------------------------------- RD-A1

    [Fact]
    public async Task A_tenant_role_is_retired_and_brought_back()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        var deactivated = await DeactivateAsync(admin, created.RoleId, "No longer used.");

        Assert.False(deactivated.IsActive);
        Assert.Equal(created.Code, deactivated.Code);
        Assert.Equal(created.Name, deactivated.Name);
        Assert.False(deactivated.IsSystemRole);
        Assert.Equal("false", await ActivityAsync(created.RoleId));

        var reactivated = await ReactivateAsync(admin, created.RoleId);

        Assert.True(reactivated.IsActive);
        Assert.Equal(created.Code, reactivated.Code);
        Assert.Equal("true", await ActivityAsync(created.RoleId));
    }

    // ---------------------------------------------------------------- RD-A2

    [Fact]
    public async Task Each_transition_is_recorded_once_with_both_sides()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        await DeactivateAsync(admin, created.RoleId, "Superseded by another role.");

        var off = await RecordAsync(created.RoleId, "RoleDeactivated");

        Assert.Equal(admin.Value, off[1]);
        Assert.Equal("Superseded by another role.", off[2]);
        Assert.Equal([("IsActive", "true")], Members((string)off[3]!));
        Assert.Equal([("IsActive", "false")], Members((string)off[4]!));

        await ReactivateAsync(admin, created.RoleId);

        var on = await RecordAsync(created.RoleId, "RoleReactivated");

        Assert.Equal(admin.Value, on[1]);
        Assert.Equal(DBNull.Value, on[2]);
        Assert.Equal([("IsActive", "false")], Members((string)on[3]!));
        Assert.Equal([("IsActive", "true")], Members((string)on[4]!));

        Assert.Equal(1, await RecordCountAsync(created.RoleId, "RoleDeactivated"));
        Assert.Equal(1, await RecordCountAsync(created.RoleId, "RoleReactivated"));
    }

    // ---------------------------------------------------------------- RD-A3

    [Fact]
    public async Task Deactivating_an_inactive_role_writes_nothing_and_records_nothing()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        await DeactivateAsync(admin, created.RoleId, "Retired.");

        var provenance = await ProvenanceAsync(created.RoleId);
        var sequence = await SequenceAsync();

        var again = await DeactivateAsync(admin, created.RoleId, "Retired again.");

        Assert.False(again.IsActive);
        Assert.Equal(provenance, await ProvenanceAsync(created.RoleId));
        Assert.Equal(sequence, await SequenceAsync());
        Assert.Equal(1, await RecordCountAsync(created.RoleId, "RoleDeactivated"));
    }

    [Fact]
    public async Task Reactivating_an_active_role_writes_nothing_and_records_nothing()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        var provenance = await ProvenanceAsync(created.RoleId);
        var sequence = await SequenceAsync();

        var again = await ReactivateAsync(admin, created.RoleId);

        Assert.True(again.IsActive);
        Assert.Equal(provenance, await ProvenanceAsync(created.RoleId));
        Assert.Equal(sequence, await SequenceAsync());
        Assert.Equal(0, await RecordCountAsync(created.RoleId, "RoleReactivated"));
    }

    // ---------------------------------------------------------------- RD-A5

    [Fact]
    public async Task A_system_role_is_refused_by_both_commands()
    {
        var admin = await SecurityAdminAsync();
        var seeded = await SeededRoleAsync("access-reviewer");
        var provenance = await ProvenanceAsync(seeded);

        Assert.Equal(NoDeactivate, await RefusalAsync(() => DeactivateAsync(admin, seeded, "Trying.")));
        Assert.Equal(NoReactivate, await RefusalAsync(() => ReactivateAsync(admin, seeded)));

        Assert.Equal("true", await ActivityAsync(seeded));
        Assert.Equal(provenance, await ProvenanceAsync(seeded));
        Assert.Equal(0, await RecordCountAsync(seeded, "RoleDeactivated"));
    }

    /// <summary>Ownership precedes the no-op: an ACTIVE seeded role still refuses reactivation.</summary>
    [Fact]
    public async Task A_system_role_already_in_the_target_state_is_still_refused()
    {
        var admin = await SecurityAdminAsync();
        var seeded = await SeededRoleAsync("user-administrator");

        Assert.Equal(NoReactivate, await RefusalAsync(() => ReactivateAsync(admin, seeded)));
    }

    // ---------------------------------------------------------------- RD-A6

    [Fact]
    public async Task An_unknown_role_is_refused_by_both_commands()
    {
        var admin = await SecurityAdminAsync();

        Assert.Equal(Unknown, await RefusalAsync(() => DeactivateAsync(admin, RoleId.New(), "Gone.")));
        Assert.Equal(Unknown, await RefusalAsync(() => ReactivateAsync(admin, RoleId.New())));
    }

    // ---------------------------------------------------------------- RD-A7

    [Fact]
    public async Task A_caller_without_role_manage_is_refused_by_both_commands()
    {
        var reviewer = await CallerAsync("aut-c5-access-reviewer", "access-reviewer");
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        Assert.Contains("permission",
            await RefusalAsync(() => DeactivateAsync(reviewer, created.RoleId, "Trying.")),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("permission",
            await RefusalAsync(() => ReactivateAsync(reviewer, created.RoleId)),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal("true", await ActivityAsync(created.RoleId));
    }

    // ------------------------------------------------------ RD-A8 and RD-A9

    /// <summary>
    /// THE CENTRAL CLAIM, through the command rather than the column: after
    /// deactivation the holder is still authorised on BOTH views and its
    /// assignment row has not moved, while the role has become ineligible for
    /// any NEW assignment.
    /// </summary>
    [Fact]
    public async Task Deactivation_changes_eligibility_and_leaves_access_untouched()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);
        var holder = await SeedUserAsync();

        await GrantAsync(admin, holder, created.RoleId);
        await GrantPermissionAsync(created.RoleId, "user.read");

        var assignment = await AssignmentRowAsync(holder, created.RoleId);

        Assert.True(await AllowedAsync(holder, "user.read"));
        Assert.Contains("user.read", await EffectiveAsync(holder));

        await DeactivateAsync(admin, created.RoleId, "Retired while held.");

        // Authorization is untouched — both views, and the row itself.
        Assert.True(
            await AllowedAsync(holder, "user.read"),
            "Deactivating a role must not remove its existing holders' access.");

        Assert.Contains("user.read", await EffectiveAsync(holder));
        Assert.Equal(assignment, await AssignmentRowAsync(holder, created.RoleId));
        Assert.Equal(1, await AssignmentCountAsync(created.RoleId));

        // Eligibility has changed.
        var second = await SeedUserAsync();

        Assert.Equal(NotGrantable, await RefusalAsync(() => GrantAsync(admin, second, created.RoleId)));
        Assert.DoesNotContain(await GrantableAsync(admin), x => x == created.RoleId.Value);
        Assert.DoesNotContain(await ListAsync(admin, includeInactive: false), x => x.RoleId == created.RoleId.Value);

        var listed = Assert.Single(
            await ListAsync(admin, includeInactive: true), x => x.RoleId == created.RoleId.Value);

        Assert.False(listed.IsActive);
        Assert.Equal(1, listed.ActiveHolderCount);

        // And reactivation restores all three.
        await ReactivateAsync(admin, created.RoleId);

        Assert.Contains(await GrantableAsync(admin), x => x == created.RoleId.Value);
        Assert.Contains(await ListAsync(admin, includeInactive: false), x => x.RoleId == created.RoleId.Value);
        Assert.True(await AllowedAsync(holder, "user.read"));
    }

    // --------------------------------------------------------------- RD-A10

    [Fact]
    public async Task An_inactive_role_can_still_be_edited_and_keeps_the_edit()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        await DeactivateAsync(admin, created.RoleId, "Retired.");

        var edited = await DispatchAsync<UpdateRoleMetadataCommand, UpdateRoleMetadataResult>(
            admin, new UpdateRoleMetadataCommand(created.RoleId, "Renamed While Retired", null));

        Assert.Equal("Renamed While Retired", edited.Name);
        Assert.False(edited.IsActive);

        var reactivated = await ReactivateAsync(admin, created.RoleId);

        Assert.Equal("Renamed While Retired", reactivated.Name);
        Assert.True(reactivated.IsActive);
    }

    // --------------------------------------------------------------- RD-A11

    [Fact]
    public async Task Nothing_but_the_role_and_its_record_is_written()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin);

        var before = await CountsAsync();

        await DeactivateAsync(admin, created.RoleId, "Retired.");

        Assert.Equal(before, await CountsAsync());
    }

    // ================================================================ harness

    private Task<CreateRoleResult> CreateAsync(UserId caller)
        => DispatchAsync<CreateRoleCommand, CreateRoleResult>(
            caller, new CreateRoleCommand($"tenant-{Guid.NewGuid():N}"[..24], "Quality Reviewer", null));

    private Task<DeactivateRoleResult> DeactivateAsync(UserId caller, RoleId roleId, string reason)
        => DispatchAsync<DeactivateRoleCommand, DeactivateRoleResult>(
            caller, new DeactivateRoleCommand(roleId, reason));

    private Task<ReactivateRoleResult> ReactivateAsync(UserId caller, RoleId roleId)
        => DispatchAsync<ReactivateRoleCommand, ReactivateRoleResult>(
            caller, new ReactivateRoleCommand(roleId));

    private Task<GrantRoleResult> GrantAsync(UserId caller, UserId target, RoleId roleId)
        => DispatchAsync<GrantRoleCommand, GrantRoleResult>(
            caller, new GrantRoleCommand(target, roleId, null, null, "AUT-C5 lifecycle tests"));

    private static List<(string Name, string? Value)> Members(string json)
        => [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(x => (x.Name, (string?)x.Value.GetRawText()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

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

    private async Task<IReadOnlyList<Guid>> GrantableAsync(UserId caller)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<GrantableRolesQuery, GrantableRolesResult>(
                new GrantableRolesQuery(), CancellationToken.None);

        return [.. result.Roles.Select(x => x.RoleId.Value)];
    }

    private async Task<IReadOnlyList<RoleSummary>> ListAsync(UserId caller, bool includeInactive)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<RoleAdministrationQuery, RoleAdministrationResult>(
                new RoleAdministrationQuery(includeInactive, false), CancellationToken.None);

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

    private Task<UserId> SecurityAdminAsync() => CallerAsync("aut-c5-security-administrator", "security-administrator");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    private async Task<RoleId> SeededRoleAsync(string code)
        => new(await ScalarAsync<Guid>($"SELECT id FROM role WHERE code = '{code}'"));

    private async Task<UserId> SeedUserAsync()
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Role', 'Holder', 'Role Holder', 'holder-{id:N}@example.test',
                     'Active', now(), '{system}', now(), '{system}');
             """);

        // Invariant 7: authorisation needs an active IDENTITY as well as an
        // active user, and AuthorizationService checks that before its
        // predicate runs. A holder without one would read as unauthorised for
        // a reason that has nothing to do with this story.
        var identity = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identity}', '{id}', 'Human', 'Local', 'Application',
                     '{identity}', 'holder-{identity:N}', 'Active', now(), '{system}');
             """);

        return new UserId(id);
    }

    /// <summary>AUT-C7 does not exist; a live grant is staged directly so the role actually carries authority.</summary>
    private async Task GrantPermissionAsync(RoleId roleId, string permissionCode)
        => await ExecuteAsync(
            $"""
             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by)
             SELECT '{Guid.NewGuid()}', '{roleId.Value}', p.id, now(), '{User.SystemUserId.Value}'
               FROM permission p WHERE p.code = '{permissionCode}';
             """);

    /// <summary>boolean::text renders "true"/"false", not psql's display "t"/"f".</summary>
    private Task<string> ActivityAsync(RoleId roleId)
        => ScalarAsync<string>($"SELECT is_active::text FROM role WHERE id = '{roleId.Value}'");

    private Task<string> ProvenanceAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"SELECT concat_ws('|', updated_at, updated_by) FROM role WHERE id = '{roleId.Value}'");

    private Task<string> AssignmentRowAsync(UserId user, RoleId roleId)
        => ScalarAsync<string>(
            $"""
            SELECT concat_ws('|', id, effective_from, coalesce(effective_to::text, ''),
                                  coalesce(revoked_at::text, ''), assignment_reason)
              FROM user_role WHERE user_id = '{user.Value}' AND role_id = '{roleId.Value}'
            """);

    private Task<long> AssignmentCountAsync(RoleId roleId)
        => ScalarAsync<long>($"SELECT count(*) FROM user_role WHERE role_id = '{roleId.Value}'");

    private Task<long> SequenceAsync()
        => ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record");

    private Task<long> RecordCountAsync(RoleId roleId, string eventType)
        => ScalarAsync<long>(
            $"""
            SELECT count(*) FROM audit.audit_record
             WHERE entity_id = '{roleId.Value}' AND event_type = '{eventType}'
            """);

    private Task<string> CountsAsync()
        => ScalarAsync<string>(
            """
            SELECT concat_ws('|',
                (SELECT count(*) FROM role),
                (SELECT count(*) FROM role_permission),
                (SELECT count(*) FROM user_role),
                (SELECT count(*) FROM permission),
                (SELECT count(*) FROM app_user))
            """);

    private async Task<object?[]> RecordAsync(RoleId roleId, string eventType)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT event_type, actor_user_id, reason, before::text, after::text
              FROM audit.audit_record
             WHERE entity_id = '{roleId.Value}' AND event_type = '{eventType}'
             ORDER BY sequence
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), $"no {eventType} record was written for the role");

        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);

        return values;
    }

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
