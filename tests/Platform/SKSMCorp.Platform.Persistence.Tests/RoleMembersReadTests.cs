using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Roles.Queries.RoleMembers;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// AUT-Q4 GetRoleMembers (docs/requirements.md, "AUT-Q4 GetRoleMembers",
/// RH-A1 to RH-A7 and RH-A10), against PostgreSQL.
///
/// WHAT THIS READ IS FOR. The access-review question — who holds this role,
/// and since when, and why. It is NOT a safety check before AUT-C5: that
/// justification was rejected at the AUT-C5/C6 gate, and nothing here revives
/// it.
///
/// THE TWO PROPERTIES WORTH BREAKING are the temporal predicate and the
/// derivation of the count. Every row below exists to hold one of them: the
/// four assignment states, an assignment straddling asOf in each direction, a
/// holder whose user is Inactive, and one holder counted once from two
/// assignments.
///
/// Its own provisioned database: these tests create roles, which the shared
/// database's catalogue-drift checks would refuse.
/// </summary>
public sealed class RoleMembersReadTests : IClassFixture<ActivationDatabase>
{
    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ActivationDatabase _database;

    public RoleMembersReadTests(ActivationDatabase database) => _database = database;

    // =============================================================== RH-A1

    /// <summary>RH-A1: exactly the ten members, the stored values, and asOf echoed.</summary>
    [Fact]
    public async Task A_member_is_answered_with_exactly_its_ten_members()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();
        var holder = await SeedUserAsync(displayName: "Zoe Holder");

        await AssignAsync(holder, roleId, from: Past, to: Past.AddYears(5),
            reason: "Onboarding, regulatory affairs associate.");

        var asOf = Past.AddDays(1);
        var result = await MembersAsync(caller, roleId, asOf);

        Assert.Equal(asOf, result.AsOf);

        var member = Assert.Single(result.Members!);

        Assert.NotEqual(Guid.Empty, member.AssignmentId.Value);
        Assert.Equal(holder, member.UserId);
        Assert.Equal("Zoe Holder", member.DisplayName);
        Assert.Equal($"holder-{holder.Value:N}@example.test", member.Email);
        Assert.Equal(UserStatus.Active, member.Status);
        Assert.Equal(Past, member.EffectiveFrom);
        Assert.Equal(Past.AddYears(5), member.EffectiveTo);
        Assert.NotEqual(default, member.AssignedAt);
        Assert.Equal(User.SystemUserId, member.AssignedBy.UserId);

        // THE GRANTER, NAMED — and named as the granter, not as the holder. A
        // non-blank assertion here let a mutant join the wrong side of the
        // assignment and survive: both names are non-blank.
        Assert.Equal(User.SystemDisplayName, member.AssignedBy.DisplayName);
        Assert.NotEqual(member.DisplayName, member.AssignedBy.DisplayName);
        Assert.Equal("Onboarding, regulatory affairs associate.", member.AssignmentReason);
    }

    /// <summary>RH-A1: ordered by display name, then assignment id.</summary>
    [Fact]
    public async Task Members_are_ordered_by_display_name()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        foreach (var name in new[] { "Yolanda Zeta", "Zoe Alpha", "Xavier Omega" })
            await AssignAsync(await SeedUserAsync(displayName: name), roleId, Past, null);

        var names = (await MembersAsync(caller, roleId)).Members!.Select(x => x.DisplayName).ToList();

        Assert.Equal(["Xavier Omega", "Yolanda Zeta", "Zoe Alpha"], names);
    }

    // =============================================================== RH-A2

    /// <summary>
    /// RH-A2: Active at asOf, and the other three states are not. Each is
    /// asserted SEPARATELY, because "the list is empty" would pass for any one
    /// of them being wrong.
    /// </summary>
    [Fact]
    public async Task Only_assignments_active_at_the_instant_are_returned()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        var active = await SeedUserAsync(displayName: "Active Holder");
        var future = await SeedUserAsync(displayName: "Future Holder");
        var ended = await SeedUserAsync(displayName: "Ended Holder");
        var revoked = await SeedUserAsync(displayName: "Revoked Holder");

        await AssignAsync(active, roleId, from: Past, to: null);
        await AssignAsync(future, roleId, from: asOf.AddDays(1), to: null);
        await AssignAsync(ended, roleId, from: Past, to: asOf.AddDays(-1));
        // Revoked BEFORE the instant asked about. A revocation after it is a
        // different case, and the holding was still held then — see
        // AsOf_answers_the_holdings_live_at_that_instant.
        await AssignAsync(revoked, roleId, from: Past, to: null, revokedAt: asOf.AddDays(-1));

        var members = (await MembersAsync(caller, roleId, asOf)).Members!;
        var held = members.Select(x => x.UserId).ToList();

        Assert.Contains(active, held);
        Assert.DoesNotContain(future, held);
        Assert.DoesNotContain(ended, held);
        Assert.DoesNotContain(revoked, held);
    }

    /// <summary>
    /// RH-A2, the boundary: the period is half-open, [from, to), exactly as
    /// the authorisation check reads it (UR12). A holding that begins at the
    /// instant is held; one that ends at it is not.
    /// </summary>
    [Fact]
    public async Task The_period_is_half_open_at_both_ends()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        var starts = await SeedUserAsync(displayName: "Starts Exactly Now");
        var ends = await SeedUserAsync(displayName: "Ends Exactly Now");

        await AssignAsync(starts, roleId, from: asOf, to: null);
        await AssignAsync(ends, roleId, from: Past, to: asOf);

        var held = (await MembersAsync(caller, roleId, asOf)).Members!.Select(x => x.UserId).ToList();

        Assert.Contains(starts, held);
        Assert.DoesNotContain(ends, held);
    }

    // =============================================================== RH-A3

    /// <summary>RH-A3: asOf projects, in both directions.</summary>
    [Fact]
    public async Task AsOf_answers_the_holdings_live_at_that_instant()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        var throughout = await SeedUserAsync(displayName: "Holds Throughout");
        var revokedLater = await SeedUserAsync(displayName: "Revoked Later");
        var grantedLater = await SeedUserAsync(displayName: "Granted Later");

        await AssignAsync(throughout, roleId, from: Past, to: null);
        await AssignAsync(revokedLater, roleId, from: Past, to: null, revokedAt: asOf.AddDays(1));
        await AssignAsync(grantedLater, roleId, from: asOf.AddDays(1), to: null);

        var atAsOf = (await MembersAsync(caller, roleId, asOf)).Members!.Select(x => x.UserId).ToList();

        // Revocation is not retroactive: at asOf that holding was still held.
        Assert.Contains(throughout, atAsOf);
        Assert.Contains(revokedLater, atAsOf);
        Assert.DoesNotContain(grantedLater, atAsOf);

        // And now, the same three the other way round.
        var now = (await MembersAsync(caller, roleId)).Members!.Select(x => x.UserId).ToList();

        Assert.Contains(throughout, now);
        Assert.DoesNotContain(revokedLater, now);
        Assert.Contains(grantedLater, now);
    }

    /// <summary>RH-A3: no asOf means the current instant, and the echo says so.</summary>
    [Fact]
    public async Task Without_asOf_the_current_instant_is_used_and_echoed()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var result = await MembersAsync(caller, roleId);

        Assert.InRange(result.AsOf, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    // =============================================================== RH-A4

    /// <summary>
    /// RH-A4, AND THE ONE MUTATION THAT MATTERS. The count is DISTINCT userId
    /// over the rows, not the row count.
    ///
    /// THIS TEST DELIBERATELY FABRICATES A STATE NO COMMAND CAN PRODUCE, and
    /// that is the whole reason it exists. GrantRole writes only global
    /// assignments, and ex_user_role_global_no_overlap forbids two overlapping
    /// periods for one user and role — so through the command surface the two
    /// numbers are necessarily equal, and a mutant swapping one for the other
    /// would survive as equivalent (RH14). A second assignment at a different
    /// scope is permitted by ex_user_role_scoped_no_overlap and makes the
    /// difference observable.
    /// </summary>
    [Fact]
    public async Task The_count_is_distinct_holders_and_not_the_row_count()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        var twice = await SeedUserAsync(displayName: "Holds It Twice");
        var once = await SeedUserAsync(displayName: "Holds It Once");

        await AssignAsync(twice, roleId, from: Past, to: null);
        await AssignAsync(twice, roleId, from: Past, to: null, scopeId: Guid.NewGuid());
        await AssignAsync(once, roleId, from: Past, to: null);

        var result = await MembersAsync(caller, roleId);

        Assert.Equal(3, result.Members!.Count);
        Assert.Equal(2, result.ActiveHolderCount);

        // The rows are assignments, not people: the same holder appears twice,
        // under two assignment ids.
        var ids = result.Members!.Where(x => x.UserId == twice).Select(x => x.AssignmentId).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
    }

    /// <summary>
    /// RH-A4: the count counts the rows it returned, and no others. A
    /// revoked holding is neither listed nor counted.
    /// </summary>
    [Fact]
    public async Task The_count_agrees_with_the_list_it_was_derived_from()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        await AssignAsync(await SeedUserAsync(), roleId, from: Past, to: null);
        await AssignAsync(await SeedUserAsync(), roleId, from: Past, to: null, revoked: true);

        var result = await MembersAsync(caller, roleId);

        Assert.Equal(1, result.ActiveHolderCount);
        Assert.Equal(
            result.Members!.Select(x => x.UserId).Distinct().Count(),
            result.ActiveHolderCount);
    }

    // =============================================================== RH-A5

    /// <summary>
    /// RH-A5: the holder's status is projection, never a filter. A departed
    /// user with a live assignment is an anomaly, and this read MAKES IT
    /// VISIBLE rather than hiding it.
    /// </summary>
    [Fact]
    public async Task An_inactive_holder_is_returned_with_its_status()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();
        var departed = await SeedUserAsync(displayName: "Departed Holder", inactive: true);

        await AssignAsync(departed, roleId, from: Past, to: null);

        var result = await MembersAsync(caller, roleId);
        var member = Assert.Single(result.Members!);

        Assert.Equal(departed, member.UserId);
        Assert.Equal(UserStatus.Inactive, member.Status);
        Assert.Equal(1, result.ActiveHolderCount);
    }

    // =============================================================== RH-A6

    /// <summary>
    /// RH-A6 and RH8: a role nobody holds is not the same answer as a role
    /// that does not exist. The reader must be able to tell them apart.
    /// </summary>
    [Fact]
    public async Task An_unheld_role_and_an_unknown_role_are_different_answers()
    {
        var caller = await ReaderAsync();
        var unheld = await MembersAsync(await ReaderAsync(), await SeedRoleAsync());

        Assert.True(unheld.RoleExists);
        Assert.Empty(unheld.Members!);
        Assert.Equal(0, unheld.ActiveHolderCount);

        var unknown = await MembersAsync(caller, RoleId.New());

        Assert.False(unknown.RoleExists);
        Assert.Null(unknown.Members);
    }

    /// <summary>
    /// RH-A6: a role held only in the past still exists and is empty now —
    /// the temporal filter must not be mistaken for "no such role".
    /// </summary>
    [Fact]
    public async Task A_role_whose_holdings_have_all_ended_still_exists()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        await AssignAsync(await SeedUserAsync(), roleId, from: Past, to: Past.AddDays(1));

        var result = await MembersAsync(caller, roleId);

        Assert.True(result.RoleExists);
        Assert.Empty(result.Members!);
    }

    // ======================================================= RH-A7, RH-A10

    /// <summary>
    /// RH-A7: BOTH permissions, and it is AND. Each direction is refused
    /// separately, because a check that happened to test only one code would
    /// pass while the other was unenforced.
    ///
    /// The seeded roles cannot express one half of this: every seeded role
    /// holding role.read also holds user.read, which is why the role.read-only
    /// caller below holds a TENANT role composed for this test. That is not an
    /// artificial case — AUT-C3 is exactly what lets a tenant compose one.
    /// </summary>
    [Fact]
    public async Task The_read_requires_role_read_and_user_read_together()
    {
        var roleId = await SeedRoleAsync();

        // Holds both: the ordinary caller.
        var member = await MembersAsync(await ReaderAsync(), roleId);
        Assert.True(member.RoleExists);

        // user.read without role.read.
        var userOnly = await CallerAsync("aut-q4-user-administrator", "user-administrator");

        // role.read without user.read: a tenant role composed for this test.
        var roleOnly = await RoleReadOnlyCallerAsync();

        foreach (var refused in new[] { userOnly, roleOnly })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => MembersAsync(refused, roleId));

            Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);

            // THE REFUSAL NAMES NEITHER CODE: which permission a caller is
            // missing is not something a refusal should disclose.
            Assert.DoesNotContain("role.read", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user.read", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }

        // And the two refusals are the same sentence, whichever half is absent.
        var first = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => MembersAsync(userOnly, roleId));

        var second = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => MembersAsync(roleOnly, roleId));

        Assert.Equal(first.Message, second.Message);
    }

    /// <summary>RH-A7: no carrier is an authentication failure, not a refusal.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_is_read()
    {
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => AnonymousAsync<RoleMembersQuery, RoleMembersResult>(
                new RoleMembersQuery(RoleId.New(), null, null)));
    }

    /// <summary>RH-A10: a read writes no audit record, whatever the outcome.</summary>
    [Fact]
    public async Task The_read_writes_no_audit_record()
    {
        var caller = await ReaderAsync();
        var roleId = await SeedRoleAsync();

        await AssignAsync(await SeedUserAsync(), roleId, from: Past, to: null);

        var before = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record");

        await MembersAsync(caller, roleId);
        await MembersAsync(caller, RoleId.New());

        var userOnly = await CallerAsync("aut-q4-user-administrator", "user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => MembersAsync(userOnly, roleId));

        Assert.Equal(before, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record"));
    }

    // ============================================================== harness

    private async Task<RoleMembersResult> MembersAsync(
        UserId caller, RoleId roleId, DateTimeOffset? asOf = null, Guid? scopeId = null)
        => await QueryAsync<RoleMembersQuery, RoleMembersResult>(
            caller, new RoleMembersQuery(roleId, asOf, scopeId));

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

    private Task<UserId> ReaderAsync() => CallerAsync("aut-q4-access-reviewer", "access-reviewer");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    /// <summary>
    /// A caller holding role.read and NOTHING ELSE, through a tenant role of
    /// this test's own. The code is deterministic so the permanent caller's
    /// assignment resolves on every run.
    /// </summary>
    private async Task<UserId> RoleReadOnlyCallerAsync()
    {
        const string Code = "aut-q4-role-read-only";
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{Guid.NewGuid()}', 'AUT-Q4 role reader', '{Code}',
                     'Holds role.read and nothing else.', false, true,
                     now(), '{system}', now(), '{system}')
             ON CONFLICT (code) DO NOTHING;
             """);

        await ExecuteAsync(
            $"""
             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by)
             SELECT '{Guid.NewGuid()}', r.id, p.id, now(), '{system}'
               FROM role r, permission p
              WHERE r.code = '{Code}' AND p.code = 'role.read'
                AND NOT EXISTS (
                    SELECT 1 FROM role_permission x
                     WHERE x.role_id = r.id AND x.permission_id = p.id AND x.revoked_at IS NULL);
             """);

        return await CallerAsync(Code, Code);
    }

    /// <summary>A tenant role, active. Returns its id.</summary>
    private async Task<RoleId> SeedRoleAsync()
    {
        var id = Guid.NewGuid();
        var code = $"tenant-{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Tenant role {code}', '{code}', NULL, false, true,
                     now(), '{system}', now(), '{system}');
             """);

        return new RoleId(id);
    }

    private async Task<UserId> SeedUserAsync(string? displayName = null, bool inactive = false)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Role', 'Holder', @displayName,
                     'holder-{id:N}@example.test', {status}, now(), '{system}', now(), '{system}');
             """,
            ("displayName", (object?)(displayName ?? $"Role Holder {id:N}"[..24]) ?? DBNull.Value));

        return new UserId(id);
    }

    private async Task AssignAsync(
        UserId user,
        RoleId role,
        DateTimeOffset from,
        DateTimeOffset? to,
        bool revoked = false,
        DateTimeOffset? revokedAt = null,
        Guid? scopeId = null,
        string reason = "Seeded for the role member read.")
    {
        var system = User.SystemUserId.Value;
        var scope = scopeId is null ? ("'Global'", "NULL") : ("'Project'", $"'{scopeId}'");
        var revocation = revoked || revokedAt is not null;

        await ExecuteAsync(
            $"""
             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                                    revoked_at, revoked_by, revocation_reason)
             VALUES ('{Guid.NewGuid()}', '{user.Value}', 'Human', '{role.Value}', {scope.Item1}, {scope.Item2},
                     @from, @to, @from, '{system}', @reason,
                     {(revocation ? "@revokedAt" : "NULL")},
                     {(revocation ? $"'{system}'" : "NULL")},
                     {(revocation ? "'Seeded revoked.'" : "NULL")});
             """,
            ("from", from),
            ("to", (object?)to ?? DBNull.Value),
            ("reason", reason),
            ("revokedAt", (object?)(revokedAt ?? (revoked ? DateTimeOffset.UtcNow : null)) ?? DBNull.Value));
    }

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
