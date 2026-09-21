using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Commands.GrantRole;
using SKSMCorp.Platform.Application.Users.Commands.RevokeRole;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// AUT-C1 GrantRole and AUT-C2 RevokeRole end to end: an established
/// administrator, dispatch through the pipeline, and the rows read back from
/// PostgreSQL (docs/requirements.md, "Role Assignment").
///
/// Authorisation effect is proven, not inferred: the real IAuthorizationService
/// is asked for the target's permissions at chosen instants. The role granted
/// is access-reviewer, and the target holds no other role, so
/// accessreview.read appears only through the assignment under test.
///
/// EVERY REFUSAL IS HELD TO "NOTHING WRITTEN": the user's assignments and the
/// audit references naming them are compared before and after.
///
/// Its own provisioned database: the administrators here become the actors of
/// audit records, which can never be deleted.
/// </summary>
public sealed class RoleAssignmentIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Reason = "Onboarding, regulatory affairs; ticket REQ-1042.";
    private const string Granted = "access-reviewer";
    private const string GrantedPermission = "accessreview.read";
    private const string OverlapRefusal =
        "The user already holds this role for this scope in an overlapping period.";

    private readonly ActivationDatabase _database;

    public RoleAssignmentIntegrationTests(ActivationDatabase database) => _database = database;

    // ================================================================ AUT-C1

    /// <summary>G1, and an omitted EffectiveFrom is the server's now (G2).</summary>
    [Fact]
    public async Task An_administrator_grants_an_active_role_to_an_active_human()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        var before = DateTimeOffset.UtcNow;
        var result = await GrantAsync(admin.UserId, target, role);
        var after = DateTimeOffset.UtcNow;

        var row = Assert.Single(await ReadAssignmentsAsync(target));

        Assert.Equal(result.UserRoleAssignmentId.Value, row.Id);
        Assert.Equal(role.Value, row.RoleId);
        Assert.Equal("Global", row.ScopeType);
        Assert.Null(row.ScopeId);
        Assert.InRange(row.EffectiveFrom, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Null(row.EffectiveTo);
        Assert.Equal(admin.UserId.Value, row.AssignedBy);
        Assert.Equal(Reason, row.AssignmentReason);
        Assert.Null(row.RevokedAt);
    }

    /// <summary>
    /// The decision and its effect are separate: a grant made now can take
    /// effect later, until a later end.
    /// </summary>
    [Fact]
    public async Task A_future_grant_records_its_decision_now_and_its_period_as_given()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));
        var to = from.AddMonths(2);

        await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted), from, to);

        var row = Assert.Single(await ReadAssignmentsAsync(target));

        Assert.Equal(from, row.EffectiveFrom);
        Assert.Equal(to, row.EffectiveTo);
        Assert.True(row.AssignedAt < from, "The decision was recorded at the effective date.");
    }

    /// <summary>G3 — the period is what authorises, at both ends.</summary>
    [Fact]
    public async Task A_grant_authorises_within_its_period_and_not_outside_it()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));
        var to = from.AddDays(30);

        await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted), from, to);

        Assert.False(await HoldsAsync(target, DateTimeOffset.UtcNow));
        Assert.False(await HoldsAsync(target, from.AddTicks(-10)));
        Assert.True(await HoldsAsync(target, from));
        Assert.True(await HoldsAsync(target, to.AddTicks(-10)));
        Assert.False(await HoldsAsync(target, to));
    }

    /// <summary>G2 — no backdating, and a grant must be able to authorise.</summary>
    [Fact]
    public async Task A_past_start_is_refused()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(
            admin.UserId, target, role, DateTimeOffset.UtcNow.AddMinutes(-5), null));
    }

    [Fact]
    public async Task An_end_equal_to_the_start_is_refused()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(
            admin.UserId, target, role, from, from));
    }

    [Fact]
    public async Task An_end_before_the_start_is_refused()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(
            admin.UserId, target, role, from, from.AddDays(-1)));
    }

    /// <summary>
    /// PostgreSQL keeps microseconds; .NET keeps 100 ns ticks. An end one tick
    /// after the start would pass a strict check made at .NET precision and be
    /// stored as an EMPTY period, which the database admits. The period is
    /// judged at the precision it is stored at, so this is refused.
    /// </summary>
    [Fact]
    public async Task An_end_less_than_a_microsecond_after_the_start_is_refused()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(
            admin.UserId, target, role, from, from.AddTicks(1)));
    }

    /// <summary>G4 — the database serialises it; the refusal is the contract's.</summary>
    [Fact]
    public async Task An_overlapping_grant_is_refused_with_the_overlap_message()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        await GrantAsync(admin.UserId, target, role);

        var failure = await AssertRefusedAsync(target, () => GrantAsync(
            admin.UserId, target, role, Hour(DateTimeOffset.UtcNow.AddDays(3)), null));

        Assert.Equal(OverlapRefusal, failure.Message);
    }

    /// <summary>A later, non-overlapping period of the same role is a separate assignment.</summary>
    [Fact]
    public async Task A_later_period_that_does_not_overlap_is_accepted()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);
        var first = Hour(DateTimeOffset.UtcNow.AddDays(2));

        await GrantAsync(admin.UserId, target, role, first, first.AddDays(10));
        await GrantAsync(admin.UserId, target, role, first.AddDays(10), null);

        Assert.Equal(2, (await ReadAssignmentsAsync(target)).Count);
    }

    /// <summary>G5 — every ineligible target or role writes nothing.</summary>
    [Theory]
    [InlineData("RetiredRole")]
    [InlineData("UnknownRole")]
    [InlineData("UnknownUser")]
    [InlineData("AgentUser")]
    [InlineData("InactiveUser")]
    public async Task An_ineligible_grant_is_refused(string scenario)
    {
        var admin = await CallerAsync("security-administrator");

        var target = scenario switch
        {
            "AgentUser" => await SeedTargetAsync(actorType: "Agent"),
            "InactiveUser" => await SeedTargetAsync(active: false),
            _ => await SeedTargetAsync(),
        };

        var role = scenario switch
        {
            "RetiredRole" => await SeedRoleAsync(active: false),
            "UnknownRole" => RoleId.New(),
            _ => await RoleIdAsync(Granted),
        };

        var userId = scenario == "UnknownUser" ? UserId.New() : target;

        await AssertRefusedAsync(target, () => GrantAsync(admin.UserId, userId, role));
    }

    /// <summary>
    /// A user who has not activated is Active: it may be granted a role, which
    /// takes effect once it can sign in.
    /// </summary>
    [Fact]
    public async Task A_user_who_has_not_activated_may_be_granted_a_role()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();

        await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted));

        Assert.Single(await ReadAssignmentsAsync(target));
    }

    /// <summary>No four-eyes rule in v1 (A1): the permission alone decides.</summary>
    [Fact]
    public async Task An_administrator_may_grant_a_role_to_themselves()
    {
        var admin = await CallerAsync("security-administrator");
        var role = await RoleIdAsync(Granted);

        var result = await GrantAsync(admin.UserId, admin.UserId, role);

        try
        {
            Assert.Contains(await ReadAssignmentsAsync(admin.UserId), x => x.Id == result.UserRoleAssignmentId.Value);
        }
        finally
        {
            await RevokeAsync(admin.UserId, result.UserRoleAssignmentId, "Self-grant test cleanup.");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_grant_reason_is_refused(string reason)
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(admin.UserId, target, role, reason: reason));
    }

    /// <summary>G7.</summary>
    [Fact]
    public async Task A_grant_writes_RoleGranted_as_the_administrator_with_the_reason()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        var result = await GrantAsync(admin.UserId, target, role);

        var record = Assert.Single(await ReadRecordsAsync(result.UserRoleAssignmentId.Value));

        Assert.Equal("RoleGranted", record.EventType);
        Assert.Equal("UserRoleAssignment", record.EntityType);
        Assert.Equal(admin.UserId.Value, record.ActorUserId);
        Assert.Equal(Reason, record.Reason);
        Assert.NotNull(record.After);
        Assert.Equal(
            [("Role", role.Value, "GrantedRole"), ("User", target.Value, "Subject")],
            await ReadRefsAsync(record.AuditId));
    }

    // ================================================================ AUT-C2

    /// <summary>R1 — ends at the revocation time, and stops authorising.</summary>
    [Fact]
    public async Task Revoking_an_active_assignment_ends_it_now_and_removes_the_permission()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var granted = await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted));

        Assert.True(await HoldsAsync(target, DateTimeOffset.UtcNow));

        var before = DateTimeOffset.UtcNow;
        await RevokeAsync(admin.UserId, granted.UserRoleAssignmentId);
        var after = DateTimeOffset.UtcNow;

        var row = Assert.Single(await ReadAssignmentsAsync(target));

        Assert.NotNull(row.EffectiveTo);
        Assert.InRange(row.EffectiveTo!.Value, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Equal(row.RevokedAt, row.EffectiveTo);
        Assert.Equal(admin.UserId.Value, row.RevokedBy);
        Assert.Equal("Left the regulatory team.", row.RevocationReason);

        Assert.False(await HoldsAsync(target, DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    /// <summary>
    /// R2 — a future assignment is closed at its start: an empty period that
    /// never authorises, and a new grant over the same dates is accepted.
    /// </summary>
    [Fact]
    public async Task Revoking_a_future_assignment_leaves_an_empty_period()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);
        var from = Hour(DateTimeOffset.UtcNow.AddDays(13));

        var granted = await GrantAsync(admin.UserId, target, role, from, null);

        await RevokeAsync(admin.UserId, granted.UserRoleAssignmentId, "Offer withdrawn.");

        var row = Assert.Single(await ReadAssignmentsAsync(target));

        Assert.Equal(from, row.EffectiveFrom);
        Assert.Equal(from, row.EffectiveTo);
        Assert.NotNull(row.RevokedAt);
        Assert.True(row.RevokedAt < from);

        Assert.False(await HoldsAsync(target, from));
        Assert.False(await HoldsAsync(target, from.AddDays(1)));

        await GrantAsync(admin.UserId, target, role, from, null);

        Assert.True(await HoldsAsync(target, from.AddDays(1)));
    }

    /// <summary>R3 — nothing written for any of them.</summary>
    [Theory]
    [InlineData("Unknown")]
    [InlineData("AlreadyRevoked")]
    [InlineData("AlreadyEnded")]
    public async Task An_assignment_that_cannot_be_revoked_is_refused(string scenario)
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        var assignment = scenario switch
        {
            "Unknown" => UserRoleId.New(),
            "AlreadyRevoked" => await RevokedAsync(admin.UserId, target, role),
            _ => await SeedEndedAssignmentAsync(target, role),
        };

        await AssertRefusedAsync(target, () => RevokeAsync(admin.UserId, assignment));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_revocation_reason_is_refused(string reason)
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var granted = await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted));

        await AssertRefusedAsync(target, () => RevokeAsync(admin.UserId, granted.UserRoleAssignmentId, reason));
    }

    /// <summary>R5.</summary>
    [Fact]
    public async Task A_revocation_writes_RoleRevoked_as_the_administrator_with_the_reason()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);
        var granted = await GrantAsync(admin.UserId, target, role);

        await RevokeAsync(admin.UserId, granted.UserRoleAssignmentId);

        var record = Assert.Single(
            await ReadRecordsAsync(granted.UserRoleAssignmentId.Value),
            x => x.EventType == "RoleRevoked");

        Assert.Equal("UserRoleAssignment", record.EntityType);
        Assert.Equal(admin.UserId.Value, record.ActorUserId);
        Assert.Equal("Left the regulatory team.", record.Reason);
        Assert.NotNull(record.Before);
        Assert.NotNull(record.After);
        Assert.Equal(
            [("Role", role.Value, "RevokedRole"), ("User", target.Value, "Subject")],
            await ReadRefsAsync(record.AuditId));
    }

    // ================================================================ A1

    /// <summary>
    /// user-administrator holds user.* but neither role.grant nor role.revoke,
    /// so this proves the SPECIFIC permission is checked.
    /// </summary>
    [Fact]
    public async Task A_caller_without_role_grant_cannot_grant()
    {
        var caller = await CallerAsync("user-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(caller.UserId, target, role));
    }

    [Fact]
    public async Task A_caller_without_role_revoke_cannot_revoke()
    {
        var admin = await CallerAsync("security-administrator");
        var caller = await CallerAsync("user-administrator");
        var target = await SeedTargetAsync();
        var granted = await GrantAsync(admin.UserId, target, await RoleIdAsync(Granted));

        await AssertRefusedAsync(target, () => RevokeAsync(caller.UserId, granted.UserRoleAssignmentId));
    }

    /// <summary>Human-only: the caller holds the permission, and only the actor type differs.</summary>
    [Fact]
    public async Task A_non_human_caller_cannot_grant_or_revoke()
    {
        var admin = await CallerAsync("security-administrator");
        var target = await SeedTargetAsync();
        var role = await RoleIdAsync(Granted);

        await AssertRefusedAsync(target, () => GrantAsync(admin.UserId, target, role, actorType: ActorType.Agent));

        var granted = await GrantAsync(admin.UserId, target, role);

        await AssertRefusedAsync(target, () => RevokeAsync(
            admin.UserId, granted.UserRoleAssignmentId, actorType: ActorType.Agent));
    }

    // ================================================================ harness

    private sealed record AssignmentRow(
        Guid Id, Guid RoleId, string ScopeType, Guid? ScopeId,
        DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
        DateTimeOffset AssignedAt, Guid AssignedBy, string? AssignmentReason,
        DateTimeOffset? RevokedAt, Guid? RevokedBy, string? RevocationReason);

    private sealed record RecordRow(
        Guid AuditId, string EventType, string EntityType, Guid? ActorUserId,
        string? Reason, string? Before, string? After);

    /// <summary>Everything either command could write, for one user.</summary>
    private sealed record Footprint(string Assignments, long AuditRefs);

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero);

    private async Task<BusinessRuleViolationException> AssertRefusedAsync(UserId target, Func<Task> dispatch)
    {
        var before = await FootprintAsync(target);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);

        Assert.Equal(before, await FootprintAsync(target));

        return failure;
    }

    private async Task<Footprint> FootprintAsync(UserId target)
    {
        var assignments = string.Join(";", (await ReadAssignmentsAsync(target)).Select(x => x.ToString()));

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM audit.audit_entity_ref
            WHERE entity_id = @user
               OR entity_id IN (SELECT id FROM user_role WHERE user_id = @user)
            """, connection);

        command.Parameters.AddWithValue("user", target.Value);

        return new Footprint(assignments, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private async Task<PermanentCaller> CallerAsync(string roleCode)
        => await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"aut-c1-{roleCode}", roleCode);

    private async Task<GrantRoleResult> GrantAsync(
        UserId caller,
        UserId target,
        RoleId role,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null,
        string reason = Reason,
        ActorType actorType = ActorType.Human)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller, actorType);

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<GrantRoleCommand, GrantRoleResult>(
                new GrantRoleCommand(target, role, effectiveFrom, effectiveTo, reason),
                CancellationToken.None);
    }

    private async Task RevokeAsync(
        UserId caller,
        UserRoleId assignment,
        string reason = "Left the regulatory team.",
        ActorType actorType = ActorType.Human)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller, actorType);

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<RevokeRoleCommand, RevokeRoleResult>(
                new RevokeRoleCommand(assignment, reason),
                CancellationToken.None);
    }

    private async Task<UserRoleId> RevokedAsync(UserId admin, UserId target, RoleId role)
    {
        var granted = await GrantAsync(admin, target, role);

        await RevokeAsync(admin, granted.UserRoleAssignmentId);

        return granted.UserRoleAssignmentId;
    }

    /// <summary>Does the target hold the granted role's permission at this instant?</summary>
    private async Task<bool> HoldsAsync(UserId target, DateTimeOffset at)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        var permissions = await scope.ServiceProvider
            .GetRequiredService<IAuthorizationService>()
            .EnumerateAsync(new EffectivePermissionsRequest(target, at), CancellationToken.None);

        return permissions.Any(x => x.Code == GrantedPermission);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private static void Establish(IServiceScope scope, UserId caller, ActorType actorType)
        => scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(
                caller,
                actorType,
                actorType == ActorType.Human ? TestActorIdentity.Human() : TestActorIdentity.NonHuman());

    /// <summary>
    /// A user with one active local identity (the authorisation check needs an
    /// active identity) and no credential or role.
    /// </summary>
    private async Task<UserId> SeedTargetAsync(bool active = true, string actorType = "Human")
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', '{actorType}', 'Role', 'Target', 'Role Target {unique[..8]}',
                  'role-target-{unique}@example.test', '{(active ? "Active" : "Inactive")}',
                  {(active ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', '{actorType}', 'Local', 'Application',
                  '{identityId}', 'role-target-{unique[..16]}', 'Active', now() - interval '1 day', '{system}');
             """);

        return new UserId(userId);
    }

    private async Task<RoleId> SeedRoleAsync(bool active)
    {
        var id = Guid.NewGuid();
        var unique = id.ToString("N")[..12];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active, created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Retired {unique}', 'retired-{unique}', 'A retired role.', false, {(active ? "true" : "false")},
                     now() - interval '1 day', '{system}', now(), '{system}')
             """);

        return new RoleId(id);
    }

    /// <summary>An assignment that ended yesterday, written directly: no command can backdate.</summary>
    private async Task<UserRoleId> SeedEndedAssignmentAsync(UserId target, RoleId role)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_role
                 (id, user_id, actor_type, role_id, scope_type, scope_id,
                  effective_from, effective_to, assigned_at, assigned_by, assignment_reason)
             VALUES
                 ('{id}', '{target.Value}', 'Human', '{role.Value}', 'Global', NULL,
                  now() - interval '10 days', now() - interval '1 day', now() - interval '10 days',
                  '{system}', 'A fixed-term assignment that has ended.')
             """);

        return new UserRoleId(id);
    }

    private async Task<RoleId> RoleIdAsync(string code)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM role WHERE code = @code", connection);

        command.Parameters.AddWithValue("code", code);

        return new RoleId((Guid)(await command.ExecuteScalarAsync())!);
    }

    private async Task<IReadOnlyList<AssignmentRow>> ReadAssignmentsAsync(UserId target)
    {
        var rows = new List<AssignmentRow>();

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, role_id, scope_type, scope_id, effective_from, effective_to,
                   assigned_at, assigned_by, assignment_reason, revoked_at, revoked_by, revocation_reason
            FROM user_role WHERE user_id = @user ORDER BY effective_from, id
            """, connection);

        command.Parameters.AddWithValue("user", target.Value);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AssignmentRow(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<RecordRow>> ReadRecordsAsync(Guid assignmentId)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT audit_id, event_type, entity_type, actor_user_id, reason, before::text, after::text
            FROM audit.audit_record WHERE entity_id = @id ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("id", assignmentId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<(string EntityType, Guid EntityId, string Role)>> ReadRefsAsync(Guid auditId)
    {
        var rows = new List<(string, Guid, string)>();

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, entity_id, ref_role FROM audit.audit_entity_ref
            WHERE audit_id = @id ORDER BY entity_type, ref_role
            """, connection);

        command.Parameters.AddWithValue("id", auditId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
