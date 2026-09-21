using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// USR-Q3 GetUserAccessSummary (docs/requirements.md, "USR-Q3
/// GetUserAccessSummary", UA-A1 to UA-A13), against PostgreSQL.
///
/// TWO THINGS ARE UNDER TEST, and neither is an algorithm.
///
/// The BOUNDARY (UA2). This read requires user.read AND role.read, which is a
/// recorded amendment to the catalogue. The negative case is the point: a
/// caller who can read the same user's core profile must NOT be able to read
/// what that user can do, because DV-7 and DV-8 already established that role
/// assignments live behind role.read and this story must not open a way round
/// them.
///
/// The AGREEMENT (UA-A13). USR-Q3 must not gain or lose a permission
/// independently of IsAllowedAsync. It is the third view's half of the same
/// guard RW-A5 put on AUT-Q7.
///
/// Its own provisioned database: these tests create roles and catalogue
/// entries, which the shared database's drift checks would refuse.
/// </summary>
public sealed class UserEffectivePermissionsTests : IClassFixture<ActivationDatabase>
{
    private static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ActivationDatabase _database;

    public UserEffectivePermissionsTests(ActivationDatabase database) => _database = database;

    // =============================================================== UA-A1

    /// <summary>UA-A1 and UA-A6: exactly three members, ordered by code, no roles.</summary>
    [Fact]
    public async Task The_set_is_answered_with_exactly_its_members_in_code_order()
    {
        var user = await SeedUserAsync();
        var role = await SeedRoleAsync();

        var later = await SeedPermissionAsync(suffix: "zzz");
        var earlier = await SeedPermissionAsync(suffix: "aaa");

        await GrantAsync(role, later);
        await GrantAsync(role, earlier);
        await AssignAsync(user, role, from: Past, to: null);

        var result = await QueryAsync(user);

        Assert.Equal(user, result.UserId);
        Assert.Equal(UserStatus.Active, result.Status);
        Assert.Equal([earlier, later], result.Permissions.Select(x => x.Code));

        var permission = result.Permissions.First();

        // UA-A6: scope travels with the permission, even though V1 has one.
        Assert.Equal("Global", permission.ScopeType);
        Assert.Null(permission.ScopeId);

        // UA-A6: there is no role member anywhere in the response.
        Assert.DoesNotContain("role", string.Join(" ", typeof(UserEffectivePermissionsResult)
            .GetProperties().Select(x => x.Name)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// UA-A2: a UNION, not a list per assignment. Two roles carrying the same
    /// permission is an ordinary configuration, and the person holds it once.
    /// </summary>
    [Fact]
    public async Task A_permission_reached_by_two_roles_appears_once()
    {
        var user = await SeedUserAsync();
        var shared = await SeedPermissionAsync();
        var only = await SeedPermissionAsync();

        var first = await SeedRoleAsync();
        var second = await SeedRoleAsync();

        await GrantAsync(first, shared);
        await GrantAsync(second, shared);
        await GrantAsync(second, only);

        await AssignAsync(user, first, from: Past, to: null);
        await AssignAsync(user, second, from: Past, to: null);

        var codes = (await QueryAsync(user)).Permissions.Select(x => x.Code).ToList();

        Assert.Equal(2, codes.Count);
        Assert.Contains(shared, codes);
        Assert.Contains(only, codes);
    }

    // =============================================================== UA-A3

    /// <summary>
    /// UA-A3: a DEACTIVATED ROLE still contributes. AUT-C5 established that
    /// deactivation changes assignment eligibility and never existing access,
    /// and this read must not quietly reverse that.
    /// </summary>
    [Fact]
    public async Task A_deactivated_role_still_contributes_its_permissions()
    {
        var user = await SeedUserAsync();
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync(isActive: false);

        await GrantAsync(role, code);
        await AssignAsync(user, role, from: Past, to: null);

        Assert.Contains(code, (await QueryAsync(user)).Permissions.Select(x => x.Code));
    }

    // =============================================================== UA-A4

    /// <summary>
    /// UA-A4 and UA-A5: only what is in effect NOW contributes. Each case is
    /// asserted separately, because an empty set would pass for any one of them
    /// being wrong.
    /// </summary>
    [Fact]
    public async Task Only_what_is_in_effect_now_contributes()
    {
        var user = await SeedUserAsync();

        var active = await SeedPermissionAsync();
        var future = await SeedPermissionAsync();
        var ended = await SeedPermissionAsync();
        var revoked = await SeedPermissionAsync();
        var revokedGrant = await SeedPermissionAsync();
        var retired = await SeedPermissionAsync(isActive: false);

        foreach (var (code, from, to, revokedAt, grantRevoked) in new[]
        {
            (active, Past, (DateTimeOffset?)null, (DateTimeOffset?)null, false),
            (future, DateTimeOffset.UtcNow.AddDays(10), null, null, false),
            (ended, Past, Past.AddDays(1), null, false),
            (revoked, Past, null, DateTimeOffset.UtcNow.AddMinutes(-1), false),
            (revokedGrant, Past, null, null, true),
            (retired, Past, null, null, false),
        })
        {
            var role = await SeedRoleAsync();
            await GrantAsync(role, code, revokedAt: grantRevoked ? DateTimeOffset.UtcNow.AddMinutes(-1) : null);
            await AssignAsync(user, role, from: from, to: to, revokedAt: revokedAt);
        }

        var codes = (await QueryAsync(user)).Permissions.Select(x => x.Code).ToList();

        Assert.Contains(active, codes);
        Assert.DoesNotContain(future, codes);
        Assert.DoesNotContain(ended, codes);
        Assert.DoesNotContain(revoked, codes);
        Assert.DoesNotContain(revokedGrant, codes);
        Assert.DoesNotContain(retired, codes);
    }

    // ======================================================== UA-A7, UA-A8

    /// <summary>
    /// UA-A7 and UA-A8, THE DISTINCTION THAT MATTERS. Three users answer an
    /// empty set for three different reasons, and `status` is what separates
    /// the one an access reviewer must not miss from the ordinary one.
    /// </summary>
    [Fact]
    public async Task An_empty_set_is_explained_by_status()
    {
        var noRoles = await SeedUserAsync();
        var departed = await SeedUserAsync(inactive: true);
        var identityless = await SeedUserAsync(identityActive: false);

        // The last two DO hold a role: their emptiness is the actor gate, not
        // an absence of assignments.
        var code = await SeedPermissionAsync();
        var role = await SeedRoleAsync();
        await GrantAsync(role, code);
        await AssignAsync(departed, role, from: Past, to: null);
        await AssignAsync(identityless, role, from: Past, to: null);

        var withoutRoles = await QueryAsync(noRoles);
        var inactive = await QueryAsync(departed);
        var withoutIdentity = await QueryAsync(identityless);

        Assert.Empty(withoutRoles.Permissions);
        Assert.Empty(inactive.Permissions);
        Assert.Empty(withoutIdentity.Permissions);

        // The distinction the contract promises: Active + empty is an ordinary
        // answer, Inactive + empty is not.
        Assert.Equal(UserStatus.Active, withoutRoles.Status);
        Assert.Equal(UserStatus.Inactive, inactive.Status);

        // And the one it deliberately does NOT signal separately (UA4): a user
        // with no active identity reads as an ordinary Active user with
        // nothing. No reason taxonomy is invented for it.
        Assert.Equal(UserStatus.Active, withoutIdentity.Status);
    }

    // ============================================================== UA-A11

    /// <summary>
    /// UA-A11: an unknown user and the System actor are the same answer, and
    /// it is the user-oriented one. No special System rule is introduced —
    /// IUserProfileReader already treats the System actor as unknown.
    /// </summary>
    [Fact]
    public async Task An_unknown_user_and_the_system_actor_are_both_refused()
    {
        foreach (var subject in new[] { UserId.New(), User.SystemUserId })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => QueryAsync(subject));

            Assert.Equal("The user does not exist.", refusal.Message);
        }
    }

    // ======================================================== UA-A9, UA-A10

    /// <summary>
    /// UA-A9 and UA-A10, THE BOUNDARY THIS STORY EXISTS TO HOLD. Both codes,
    /// and it is AND.
    ///
    /// The user.read-only caller is the case the amendment was written for: a
    /// user administrator can read this very user's core profile, and must not
    /// be able to read what they can do. Asserted against the SEEDED
    /// user-administrator composition, as DV-8 does.
    /// </summary>
    [Fact]
    public async Task The_read_requires_user_read_and_role_read_together()
    {
        var subject = await SeedUserAsync();

        // Holds both.
        Assert.Equal(subject, (await QueryAsync(subject)).UserId);

        var userOnly = await CallerAsync("usr-q3-user-administrator", "user-administrator");
        var roleOnly = await RoleReadOnlyCallerAsync();

        var refusals = new List<string>();

        foreach (var refused in new[] { userOnly, roleOnly })
        {
            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => QueryAsync(subject, caller: refused));

            Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user.read", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("role.read", refusal.Message, StringComparison.OrdinalIgnoreCase);

            refusals.Add(refusal.Message);
        }

        Assert.Equal(refusals[0], refusals[1]);
    }

    /// <summary>
    /// UA-A10, stated as the bypass it prevents: the user administrator CAN
    /// read the profile of the very user whose effective permissions it is
    /// refused. If this ever passes in both directions, the boundary DV-7 and
    /// DV-8 protect has been opened from a second door.
    /// </summary>
    [Fact]
    public async Task The_caller_refused_here_can_still_read_the_same_users_profile()
    {
        var subject = await SeedUserAsync();
        var userOnly = await CallerAsync("usr-q3-user-administrator", "user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => QueryAsync(subject, caller: userOnly));

        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(userOnly, ActorType.Human, TestActorIdentity.Human());

        var profile = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<Application.Users.Queries.UserProfile.UserProfileQuery,
                Application.Users.Queries.UserProfile.UserProfileResult>(
                new Application.Users.Queries.UserProfile.UserProfileQuery(subject),
                CancellationToken.None);

        Assert.Equal(subject, profile.UserId);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_is_read()
    {
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => AnonymousAsync(new UserEffectivePermissionsQuery(UserId.New())));
    }

    // ============================================================== UA-A12

    [Fact]
    public async Task The_read_writes_no_audit_record()
    {
        var user = await SeedUserAsync();
        var before = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record");

        await QueryAsync(user);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => QueryAsync(UserId.New()));

        Assert.Equal(before, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record"));
    }

    // ============================================================== UA-A13

    /// <summary>
    /// UA-A13, THE LOAD-BEARING TEST. USR-Q3 must not gain or lose a permission
    /// independently of the decision the pipeline makes, so it is checked in
    /// BOTH directions:
    ///
    ///   every code IN the set      -> IsAllowedAsync allows it
    ///   every catalogue code NOT in it -> IsAllowedAsync denies it
    ///
    /// The second direction is the one that catches a set that quietly grew.
    /// This is the third view's half of the guard RW-A5 put on AUT-Q7.
    /// </summary>
    [Fact]
    public async Task The_set_agrees_with_the_decision_in_both_directions()
    {
        var user = await SeedUserAsync();

        var held = await SeedPermissionAsync();
        var heldByDeadRole = await SeedPermissionAsync();
        var notHeld = await SeedPermissionAsync();
        var retired = await SeedPermissionAsync(isActive: false);

        var live = await SeedRoleAsync();
        var deactivated = await SeedRoleAsync(isActive: false);
        var expired = await SeedRoleAsync();

        await GrantAsync(live, held);
        await GrantAsync(deactivated, heldByDeadRole);
        await GrantAsync(expired, notHeld);
        await GrantAsync(live, retired);

        await AssignAsync(user, live, from: Past, to: null);
        await AssignAsync(user, deactivated, from: Past, to: null);
        await AssignAsync(user, expired, from: Past, to: Past.AddDays(1));

        var result = await QueryAsync(user);
        var inTheSet = result.Permissions.Select(x => x.Code).ToHashSet();

        await using var provider = Provider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        async Task<bool> AllowedAsync(string code)
            => (await resolver.IsAllowedAsync(
                new AuthorizationRequest(user, code, now, "Global", null),
                CancellationToken.None)).IsAllowed;

        foreach (var code in new[] { held, heldByDeadRole, notHeld, retired })
        {
            Assert.True(
                inTheSet.Contains(code) == await AllowedAsync(code),
                $"USR-Q3 and IsAllowedAsync disagree about {code}: "
                + $"in the set = {inTheSet.Contains(code)}.");
        }

        // The suite proves nothing if every code answers alike.
        Assert.Contains(held, inTheSet);
        Assert.Contains(heldByDeadRole, inTheSet);
        Assert.DoesNotContain(notHeld, inTheSet);
        Assert.DoesNotContain(retired, inTheSet);
    }

    // ============================================================== harness

    private async Task<UserEffectivePermissionsResult> QueryAsync(UserId subject, UserId? caller = null)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller ?? await ReaderAsync(), ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserEffectivePermissionsQuery, UserEffectivePermissionsResult>(
                new UserEffectivePermissionsQuery(subject), CancellationToken.None);
    }

    private async Task<UserEffectivePermissionsResult> AnonymousAsync(UserEffectivePermissionsQuery query)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserEffectivePermissionsQuery, UserEffectivePermissionsResult>(
                query, CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private Task<UserId> ReaderAsync() => CallerAsync("usr-q3-access-reviewer", "access-reviewer");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    /// <summary>A caller holding role.read and nothing else, through a tenant role.</summary>
    private async Task<UserId> RoleReadOnlyCallerAsync()
    {
        const string Code = "usr-q3-role-read-only";
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO role (id, name, code, description, is_system_role, is_active,
                               created_at, created_by, updated_at, updated_by)
             VALUES ('{Guid.NewGuid()}', 'USR-Q3 role reader', '{Code}',
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

    private async Task<string> SeedPermissionAsync(bool isActive = true, string? suffix = null)
    {
        var id = Guid.NewGuid();
        var code = suffix is null ? $"zzq3.{id:N}"[..20] : $"zzq3.{suffix}.{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO permission (id, code, name, description, resource, action,
                                     requires_human_actor, is_active, created_at, created_by)
             VALUES ('{id}', '{code}', 'Seeded {code}', NULL, 'ZzQ3', 'Read',
                     false, {(isActive ? "true" : "false")}, now(), '{system}');
             """);

        return code;
    }

    private async Task<RoleId> SeedRoleAsync(bool isActive = true)
    {
        var id = Guid.NewGuid();
        var code = $"q3role-{id:N}"[..24];
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

    private async Task GrantAsync(RoleId role, string permissionCode, DateTimeOffset? revokedAt = null)
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
            ("granted", Past),
            ("revoked", (object?)revokedAt ?? DBNull.Value));
    }

    /// <summary>
    /// A user AND an identity. Invariant 7 gates on both, so seeding only
    /// app_user would make these tests fail on their own setup.
    /// </summary>
    private async Task<UserId> SeedUserAsync(bool inactive = false, bool identityActive = true)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   deactivated_at, deactivated_by, created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Access', 'Subject', 'Access Subject {id:N}'::text,
                     'subject-{id:N}@example.test', {status}, now(), '{system}', now(), '{system}');
             """);

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
                     '{identityId}', 'q3-{id:N}', {identityStatus}, now(), '{system}');
             """);

        return new UserId(id);
    }

    private async Task AssignAsync(
        UserId user, RoleId role, DateTimeOffset from, DateTimeOffset? to, DateTimeOffset? revokedAt = null)
    {
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_role (id, user_id, actor_type, role_id, scope_type, scope_id,
                                    effective_from, effective_to, assigned_at, assigned_by, assignment_reason,
                                    revoked_at, revoked_by, revocation_reason)
             VALUES ('{Guid.NewGuid()}', '{user.Value}', 'Human', '{role.Value}', 'Global', NULL,
                     @from, @to, @from, '{system}', 'Seeded for the access summary.',
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
