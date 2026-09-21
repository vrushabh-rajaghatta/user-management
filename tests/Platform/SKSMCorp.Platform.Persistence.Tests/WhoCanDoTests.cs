using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// AUT-Q7 WhoCanDo (docs/requirements.md, "AUT-Q7 WhoCanDo", RW-A1 to RW-A8
/// and RW-A10), against PostgreSQL.
///
/// THE GATE WAS asOf, AND SO IS THIS FILE. Of the five facts that decide
/// authorisation the schema dates only three, and the contract resolves those
/// three at the instant while naming the other two as current-state gates
/// (RW3, RW4, RW9). Every temporal test below breaks ONE of the three, in both
/// directions, because "the list is empty" would pass for any of them being
/// wrong.
///
/// THE OTHER HALF IS THAT THE THREE VIEWS CANNOT DISAGREE (RW-A5). AUT-Q7 is a
/// third view over the resolver, not a second authorisation rule, and the
/// agreement test is what keeps it one evaluation.
///
/// Its own provisioned database: these tests create roles and catalogue
/// entries, which the shared database's drift checks would refuse.
/// </summary>
public sealed class WhoCanDoTests : IClassFixture<ActivationDatabase>
{
    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ActivationDatabase _database;

    public WhoCanDoTests(ActivationDatabase database) => _database = database;

    // =============================================================== RW-A1

    /// <summary>RW-A1: exactly the five members, ordered by display name.</summary>
    [Fact]
    public async Task A_holder_is_answered_with_exactly_its_five_members()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        await GrantAsync(role, code, Past);

        var user = await SeedUserAsync(displayName: "Zoe Holder");
        await AssignAsync(user, role, from: Past, to: null);

        var holders = await WhoCanDoAsync(code, Past.AddDays(1));
        var holder = Assert.Single(holders!);

        Assert.Equal(user, holder.UserId);
        Assert.Equal("Zoe Holder", holder.DisplayName);
        Assert.Equal($"holder-{user.Value:N}@example.test", holder.Email);
        Assert.Equal(UserStatus.Active, holder.Status);

        var authorising = Assert.Single(holder.Roles);

        Assert.Equal(role, authorising.RoleId);
        Assert.False(string.IsNullOrWhiteSpace(authorising.Name));
    }

    [Fact]
    public async Task Holders_are_ordered_by_display_name()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        await GrantAsync(role, code, Past);

        foreach (var name in new[] { "Yolanda Zeta", "Zoe Alpha", "Xavier Omega" })
            await AssignAsync(await SeedUserAsync(displayName: name), role, Past, null);

        var names = (await WhoCanDoAsync(code)).Select(x => x.DisplayName).ToList();

        Assert.Equal(["Xavier Omega", "Yolanda Zeta", "Zoe Alpha"], names);
    }

    // ============================================== RW-A2, the dated facts

    /// <summary>
    /// RW-A2, the grant's own lifecycle — the condition the shared predicate
    /// did not have at all. A grant made AFTER the instant asked about did not
    /// confer authority at that instant, and saying otherwise would answer an
    /// inspection question with a permission granted afterwards.
    /// </summary>
    [Fact]
    public async Task A_grant_made_after_the_instant_did_not_authorise_then()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        await GrantAsync(role, code, grantedAt: asOf.AddDays(1));
        await AssignAsync(await SeedUserAsync(), role, from: Past, to: null);

        Assert.Empty(await WhoCanDoAsync(code, asOf));

        // And by now it does authorise, so the row itself is sound.
        Assert.Single(await WhoCanDoAsync(code));
    }

    /// <summary>RW-A2: a grant revoked AFTER the instant was live then.</summary>
    [Fact]
    public async Task A_grant_revoked_after_the_instant_still_authorised_then()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        await GrantAsync(role, code, Past, revokedAt: asOf.AddDays(1));
        var user = await SeedUserAsync();
        await AssignAsync(user, role, from: Past, to: null);

        Assert.Equal(user, Assert.Single(await WhoCanDoAsync(code, asOf)).UserId);

        // Revocation is not retroactive, but it IS effective afterwards.
        Assert.Empty(await WhoCanDoAsync(code));
    }

    /// <summary>RW-A2: a grant revoked BEFORE the instant had already gone.</summary>
    [Fact]
    public async Task A_grant_revoked_before_the_instant_did_not_authorise_then()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        await GrantAsync(role, code, Past, revokedAt: asOf.AddDays(-1));
        await AssignAsync(await SeedUserAsync(), role, from: Past, to: null);

        Assert.Empty(await WhoCanDoAsync(code, asOf));
    }

    /// <summary>
    /// RW-A2: the assignment's revocation, resolved at the instant — the same
    /// correction AUT-Q4 forced on the domain derivation, here in SQL.
    /// </summary>
    [Fact]
    public async Task An_assignment_revoked_after_the_instant_still_authorised_then()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        await GrantAsync(role, code, Past);

        var user = await SeedUserAsync();
        await AssignAsync(user, role, from: Past, to: null, revokedAt: asOf.AddDays(1));

        Assert.Equal(user, Assert.Single(await WhoCanDoAsync(code, asOf)).UserId);
        Assert.Empty(await WhoCanDoAsync(code));
    }

    /// <summary>RW-A2: and the assignment's period, at both edges.</summary>
    [Fact]
    public async Task The_assignment_period_is_half_open_at_the_instant()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        var asOf = Past.AddDays(30);

        await GrantAsync(role, code, Past);

        var starts = await SeedUserAsync(displayName: "Starts Exactly Then");
        var ends = await SeedUserAsync(displayName: "Ends Exactly Then");

        await AssignAsync(starts, role, from: asOf, to: null);
        await AssignAsync(ends, role, from: Past, to: asOf);

        var held = (await WhoCanDoAsync(code, asOf)).Select(x => x.UserId).ToList();

        Assert.Contains(starts, held);
        Assert.DoesNotContain(ends, held);
    }

    // ========================================== RW-A3, the undated facts

    /// <summary>
    /// RW-A3 and RW-A9: a retired permission EXISTS. It is not unknown, and it
    /// is not resurrected — the shared predicate requires an active permission,
    /// so it answers nobody, and the caller can tell that apart from "nobody
    /// holds it" by the echoed IsActive.
    /// </summary>
    [Fact]
    public async Task A_retired_permission_exists_and_answers_nobody()
    {
        var code = await SeedPermissionAsync(isActive: false);
        var role = await SeedRoleAsync();
        await GrantAsync(role, code, Past);
        await AssignAsync(await SeedUserAsync(), role, from: Past, to: null);

        var result = await QueryAsync(code);

        Assert.True(result.PermissionExists);
        Assert.NotNull(result.Permission);
        Assert.False(result.Permission!.IsActive);
        Assert.Empty(result.Holders!);

        // NOT reconstructed at an earlier instant either: the catalogue keeps
        // no history, and the contract says so rather than inventing one.
        Assert.Empty((await QueryAsync(code, Past.AddDays(1))).Holders!);
    }

    /// <summary>
    /// RW-A3: user and identity status are CURRENT gates. A holder inactive now
    /// is absent whatever the instant says, because the schema retains no
    /// status history to reconstruct from (RW4).
    /// </summary>
    [Fact]
    public async Task A_holder_inactive_now_is_absent_whatever_the_instant()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        await GrantAsync(role, code, Past);

        var departed = await SeedUserAsync(inactive: true);
        var identityless = await SeedUserAsync(identityActive: false);

        await AssignAsync(departed, role, from: Past, to: null);
        await AssignAsync(identityless, role, from: Past, to: null);

        foreach (var asOf in new DateTimeOffset?[] { null, Past.AddDays(1) })
        {
            var held = (await WhoCanDoAsync(code, asOf)).Select(x => x.UserId).ToList();

            Assert.DoesNotContain(departed, held);
            Assert.DoesNotContain(identityless, held);
        }
    }

    // =============================================================== RW-A4

    /// <summary>
    /// RW-A4: one row per USER, with every authorising role named — the
    /// opposite of AUT-Q4's per-assignment choice, and the answer to "who can
    /// do this, and why?".
    /// </summary>
    [Fact]
    public async Task A_holder_reached_by_several_roles_is_one_row_naming_them_all()
    {
        var code = await SeedPermissionAsync();
        var first = await SeedRoleAsync();
        var second = await SeedRoleAsync();
        var third = await SeedRoleAsync();

        await GrantAsync(first, code, Past);
        await GrantAsync(second, code, Past);
        await GrantAsync(third, code, Past);

        var many = await SeedUserAsync(displayName: "Holds It Three Ways");
        var one = await SeedUserAsync(displayName: "Zulu One Way");

        await AssignAsync(many, first, from: Past, to: null);
        await AssignAsync(many, second, from: Past, to: null);
        await AssignAsync(many, third, from: Past, to: null);
        await AssignAsync(one, first, from: Past, to: null);

        var holders = await WhoCanDoAsync(code);

        Assert.Equal(2, holders.Count);

        var holder = holders.Single(x => x.UserId == many);

        Assert.Equal(3, holder.Roles.Count);
        Assert.Equal(3, holder.Roles.Select(x => x.RoleId).Distinct().Count());
        Assert.Equal([first, second, third], holder.Roles.Select(x => x.RoleId).OrderBy(x => x.Value));

        Assert.Single(holders.Single(x => x.UserId == one).Roles);
    }

    // =============================================================== RW-A5

    /// <summary>
    /// RW-A5, THE LOAD-BEARING TEST. The three views are one evaluation, so for
    /// the same user, permission and instant they must agree: WhoCanDo names a
    /// user exactly when IsAllowedAsync allows them and exactly when
    /// EnumerateAsync lists the code.
    ///
    /// Each case below is a state where a careless third implementation would
    /// diverge from the other two.
    /// </summary>
    [Fact]
    public async Task The_three_views_agree_for_every_state()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        await GrantAsync(role, code, Past);

        var retiredRole = await SeedRoleAsync(isActive: false);
        await GrantAsync(retiredRole, code, Past);

        var at = Past.AddDays(30);

        var cases = new (string Label, UserId User)[]
        {
            ("an ordinary holder", await SeedUserAsync()),
            ("a holder of a DEACTIVATED role", await SeedUserAsync()),
            ("a future assignment", await SeedUserAsync()),
            ("an ended assignment", await SeedUserAsync()),
            ("an assignment revoked before the instant", await SeedUserAsync()),
            ("an assignment revoked after the instant", await SeedUserAsync()),
            ("an inactive user", await SeedUserAsync(inactive: true)),
            ("a user with no active identity", await SeedUserAsync(identityActive: false)),
            ("someone holding nothing at all", await SeedUserAsync()),
        };

        await AssignAsync(cases[0].User, role, from: Past, to: null);
        await AssignAsync(cases[1].User, retiredRole, from: Past, to: null);
        await AssignAsync(cases[2].User, role, from: at.AddDays(1), to: null);
        await AssignAsync(cases[3].User, role, from: Past, to: at.AddDays(-1));
        await AssignAsync(cases[4].User, role, from: Past, to: null, revokedAt: at.AddDays(-1));
        await AssignAsync(cases[5].User, role, from: Past, to: null, revokedAt: at.AddDays(1));
        await AssignAsync(cases[6].User, role, from: Past, to: null);
        await AssignAsync(cases[7].User, role, from: Past, to: null);

        await using var provider = Provider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var named = (await resolver.WhoCanDoAsync(
                new WhoCanDoRequest(code, at, "Global", null), CancellationToken.None))!
            .Select(x => x.UserId)
            .ToHashSet();

        foreach (var (label, user) in cases)
        {
            var allowed = (await resolver.IsAllowedAsync(
                new AuthorizationRequest(user, code, at, "Global", null),
                CancellationToken.None)).IsAllowed;

            var enumerated = (await resolver.EnumerateAsync(
                    new EffectivePermissionsRequest(user, at), CancellationToken.None))
                .Any(x => x.Code == code);

            Assert.True(
                allowed == enumerated,
                $"IsAllowed and Enumerate disagree for {label}: {allowed} vs {enumerated}.");

            Assert.True(
                allowed == named.Contains(user),
                $"IsAllowed and WhoCanDo disagree for {label}: {allowed} vs {named.Contains(user)}.");
        }

        // The suite is worthless if every case answers the same way.
        Assert.Contains(cases[0].User, named);
        Assert.Contains(cases[1].User, named);
        Assert.DoesNotContain(cases[2].User, named);
        Assert.DoesNotContain(cases[8].User, named);
    }

    /// <summary>
    /// RW-A6, pinned separately because generalising the predicate is exactly
    /// when someone adds the filter back "because it looks safer": deactivating
    /// a role changes assignment ELIGIBILITY, never existing access (AUT-C5).
    /// </summary>
    [Fact]
    public async Task A_holder_of_a_deactivated_role_still_appears()
    {
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync(isActive: false);
        await GrantAsync(role, code, Past);

        var user = await SeedUserAsync();
        await AssignAsync(user, role, from: Past, to: null);

        Assert.Equal(user, Assert.Single(await WhoCanDoAsync(code)).UserId);
    }

    // =============================================== RW-A9, RW-A10, RW-A12

    /// <summary>RW-A10: an unknown code is not an empty list.</summary>
    [Fact]
    public async Task An_unheld_permission_and_an_unknown_code_are_different_answers()
    {
        var unheld = await QueryAsync(await SeedPermissionAsync());

        Assert.True(unheld.PermissionExists);
        Assert.NotNull(unheld.Permission);
        Assert.True(unheld.Permission!.IsActive);
        Assert.Empty(unheld.Holders!);

        var unknown = await QueryAsync("no.such.permission");

        Assert.False(unknown.PermissionExists);
        Assert.Null(unknown.Holders);
    }

    /// <summary>
    /// RW-A9: BOTH permissions, and it is AND. The role.read-only caller holds
    /// a TENANT role composed for this test, because every seeded role holding
    /// role.read also holds user.read — the same gap AUT-Q4 found.
    /// </summary>
    [Fact]
    public async Task The_read_requires_role_read_and_user_read_together()
    {
        var code = await SeedPermissionAsync();

        Assert.True((await QueryAsync(code)).PermissionExists);

        var userOnly = await CallerAsync("aut-q7-user-administrator", "user-administrator");
        var roleOnly = await RoleReadOnlyCallerAsync();

        var refusals = new List<string>();

        foreach (var refused in new[] { userOnly, roleOnly })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => QueryAsync(code, caller: refused));

            Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("role.read", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user.read", refusal.Message, StringComparison.OrdinalIgnoreCase);

            refusals.Add(refusal.Message);
        }

        Assert.Equal(refusals[0], refusals[1]);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_is_read()
    {
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => AnonymousAsync(new WhoCanDoQuery("user.read", null, null, null)));
    }

    /// <summary>RW-A11: the scope parameters are kept, and refused.</summary>
    [Fact]
    public async Task A_non_global_scope_is_refused()
    {
        var code = await SeedPermissionAsync();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => QueryAsync(code, scopeType: "Project"));

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => QueryAsync(code, scopeId: Guid.NewGuid()));
    }

    /// <summary>RW-A12: a read writes no audit record.</summary>
    [Fact]
    public async Task The_read_writes_no_audit_record()
    {
        var code = await SeedPermissionAsync();
        var before = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record");

        await QueryAsync(code);
        await QueryAsync("no.such.permission");

        Assert.Equal(before, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record"));
    }

    // ============================================================== harness

    private async Task<IReadOnlyList<PermissionHolder>> WhoCanDoAsync(
        string code, DateTimeOffset? asOf = null)
        => (await QueryAsync(code, asOf)).Holders
            ?? throw new InvalidOperationException("The permission does not exist.");

    private async Task<WhoCanDoResult> QueryAsync(
        string code,
        DateTimeOffset? asOf = null,
        UserId? caller = null,
        string? scopeType = null,
        Guid? scopeId = null)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller ?? await ReaderAsync(), ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<WhoCanDoQuery, WhoCanDoResult>(
                new WhoCanDoQuery(code, asOf, scopeType, scopeId), CancellationToken.None);
    }

    private async Task<WhoCanDoResult> AnonymousAsync(WhoCanDoQuery query)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<WhoCanDoQuery, WhoCanDoResult>(query, CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private Task<UserId> ReaderAsync() => CallerAsync("aut-q7-access-reviewer", "access-reviewer");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    /// <summary>A caller holding role.read and nothing else, through a tenant role.</summary>
    private async Task<UserId> RoleReadOnlyCallerAsync()
    {
        const string Code = "aut-q7-role-read-only";
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{Guid.NewGuid()}', 'AUT-Q7 role reader', '{Code}',
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

    /// <summary>A catalogue entry of this test's own. Returns its code.</summary>
    private async Task<string> SeedPermissionAsync(bool isActive = true)
    {
        var id = Guid.NewGuid();
        var code = $"zzq7.{id:N}"[..20];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO permission (id, code, name, description, resource, action,
                                     requires_human_actor, is_active, created_at, created_by)
             VALUES ('{id}', '{code}', 'Seeded {code}', NULL, 'ZzQ7', 'Read',
                     false, {(isActive ? "true" : "false")}, now(), '{system}');
             """);

        return code;
    }

    private async Task<RoleId> SeedRoleAsync(bool isActive = true)
    {
        var id = Guid.NewGuid();
        var code = $"q7role-{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Tenant role {code}', '{code}', NULL, false,
                     {(isActive ? "true" : "false")}, now(), '{system}', now(), '{system}');
             """);

        return new RoleId(id);
    }

    private async Task GrantAsync(
        RoleId role, string permissionCode, DateTimeOffset? grantedAt = null, DateTimeOffset? revokedAt = null)
    {
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by, revoked_at, revoked_by)
             SELECT '{Guid.NewGuid()}', '{role.Value}', p.id, @granted, '{system}', @revoked,
                    {(revokedAt is null ? "NULL" : $"'{system}'")}
               FROM permission p
              WHERE p.code = '{permissionCode}';
             """,
            ("granted", grantedAt ?? Past),
            ("revoked", (object?)revokedAt ?? DBNull.Value));
    }

    /// <summary>
    /// A user AND an active identity. Invariant 7 gates on both, so seeding
    /// only app_user would make these tests fail on their own setup — the
    /// lesson AUT-C5 learned the hard way.
    /// </summary>
    private async Task<UserId> SeedUserAsync(
        string? displayName = null, bool inactive = false, bool identityActive = true)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Perm', 'Holder', @displayName,
                     'holder-{id:N}@example.test', {status}, now(), '{system}', now(), '{system}');
             """,
            ("displayName", (object?)(displayName ?? $"Holder {id:N}"[..20]) ?? DBNull.Value));

        var identityId = Guid.NewGuid();
        var identityStatus = identityActive
            ? "'Active', NULL, NULL"
            : $"'Inactive', now() - interval '1 day', '{system}'";

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, deactivated_at, deactivated_by,
                                        created_at, created_by)
             VALUES ('{identityId}', '{id}', 'Human', 'Local', 'Application',
                     '{identityId}', 'q7-{id:N}', {identityStatus}, now(), '{system}');
             """);

        return new UserId(id);
    }

    private async Task AssignAsync(
        UserId user,
        RoleId role,
        DateTimeOffset from,
        DateTimeOffset? to,
        DateTimeOffset? revokedAt = null)
    {
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                                    revoked_at, revoked_by, revocation_reason)
             VALUES ('{Guid.NewGuid()}', '{user.Value}', 'Human', '{role.Value}', 'Global', NULL,
                     @from, @to, @from, '{system}', 'Seeded for the reverse lookup.',
                     {(revokedAt is null ? "NULL" : "@revokedAt")},
                     {(revokedAt is null ? "NULL" : $"'{system}'")},
                     {(revokedAt is null ? "NULL" : "'Seeded revoked.'")});
             """,
            ("from", from),
            ("to", (object?)to ?? DBNull.Value),
            ("revokedAt", (object?)revokedAt ?? DBNull.Value));
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
