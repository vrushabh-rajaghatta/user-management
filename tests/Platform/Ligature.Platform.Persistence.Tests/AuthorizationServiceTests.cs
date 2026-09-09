using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Inv. 7 — "no single record authorises alone" — exercised against a real
/// database, because every clause under test is a join or a partial predicate
/// that an in-memory provider would answer differently.
///
/// Each test starts from a fully authorised fixture and breaks exactly ONE
/// condition, so a failure names the clause that stopped working. The positive
/// case is asserted inside each, which is what stops a test from passing
/// because the fixture was silently broken.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class AuthorizationServiceTests
{

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------- baseline

    [Fact]
    public async Task A_fully_qualified_actor_is_allowed()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    [Fact]
    public async Task An_unknown_user_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { UserId = UserId.New() },
                CancellationToken.None));
        });
    }

    /// <summary>
    /// The assignment must belong to THIS user. An unknown id is not enough to
    /// prove that: it short-circuits on the actor lookup before the join is
    /// ever reached. This bystander is active, has an active identity, and
    /// holds no assignment — so only the join's user predicate can deny them.
    ///
    /// Without it, any active user would inherit every other user's authority.
    /// </summary>
    [Fact]
    public async Task An_active_user_does_not_inherit_another_users_assignment()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var bystanderId = await SeedBystanderAsync(context);

            try
            {
                Assert.True(
                    await IsAllowedAsync(service,
                        fixture.Request(), CancellationToken.None),
                    "The assignment holder is allowed.");

                Assert.False(
                    await IsAllowedAsync(service,
                        fixture.Request() with { UserId = bystanderId },
                        CancellationToken.None),
                    "A user holding no assignment must not borrow one.");
            }
            finally
            {
                await DeleteActorAsync(bystanderId);
            }
        });
    }

    // ------------------------------------------------------- 1. user active

    /// <summary>
    /// The spec's named defect: "an active assignment belonging to a
    /// deactivated user must grant nothing — the omission of that condition is
    /// exactly the defect that allows a departed employee to continue acting."
    /// </summary>
    [Fact]
    public async Task A_deactivated_user_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));

            await ExecuteAsync(
                "UPDATE app_user SET status = 'Inactive' WHERE id = @id",
                fixture.UserId.Value);

            Assert.False(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    // --------------------------------------------------- 2. identity active

    [Fact]
    public async Task A_user_with_no_active_identity_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));

            await ExecuteAsync(
                "UPDATE user_identity SET status = 'Inactive' WHERE user_id = @id",
                fixture.UserId.Value);

            Assert.False(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    // --------------------------------------------------------- 3/4. temporal

    [Fact]
    public async Task An_assignment_that_has_not_started_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var request = fixture.Request() with
            {
                At = fixture.EffectiveFrom.AddSeconds(-1),
            };

            Assert.False(await IsAllowedAsync(service,
                request, CancellationToken.None));

            Assert.True(await IsAllowedAsync(service,
                request with { At = fixture.EffectiveFrom },
                CancellationToken.None));
        });
    }

    [Fact]
    public async Task An_expired_assignment_is_denied_at_and_after_its_end()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var expiry = Now.AddDays(30);

            await ExecuteAsync(
                "UPDATE user_role SET effective_to = @value WHERE id = @id",
                fixture.AssignmentId.Value,
                expiry);

            Assert.True(await IsAllowedAsync(service,
                fixture.Request() with { At = expiry.AddSeconds(-1) },
                CancellationToken.None));

            // Half-open interval: the end instant is already outside.
            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { At = expiry },
                CancellationToken.None));

            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { At = expiry.AddSeconds(1) },
                CancellationToken.None));
        });
    }

    // -------------------------------------------------------- 5. RevokedAt

    /// <summary>
    /// UR12's literal text omits RevokedAt, and UR3 only requires EffectiveTo
    /// to be non-null when revoked — not to be in the past. So a revoked
    /// assignment with a future EffectiveTo satisfies UR12 verbatim. Without
    /// the explicit RevokedAt filter, this row would still authorise.
    /// </summary>
    [Fact]
    public async Task A_revoked_assignment_is_denied_even_with_a_future_end_date()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));

            await ExecuteAsync(
                """
                UPDATE user_role
                SET revoked_at = @value,
                    revoked_by = @actor,
                    revocation_reason = 'Revoked during test',
                    effective_to = @future
                WHERE id = @id
                """,
                fixture.AssignmentId.Value,
                Now,
                extra: (User.SystemUserId.Value, Now.AddYears(5)));

            Assert.False(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_revoked_role_permission_grant_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));

            await ExecuteAsync(
                """
                UPDATE role_permission
                SET revoked_at = @value, revoked_by = @actor
                WHERE id = @id
                """,
                fixture.GrantId.Value,
                Now,
                extra: (User.SystemUserId.Value, (object?)null));

            Assert.False(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    // ------------------------------------------------------------- 6/7. scope

    [Fact]
    public async Task A_different_scope_type_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { ScopeType = "Product" },
                CancellationToken.None));
        });
    }

    /// <summary>
    /// Exact match, no hierarchy: a Global assignment does not yet imply a
    /// narrower scope. Whoever introduces the first non-Global scope decides
    /// whether inheritance applies.
    /// </summary>
    [Fact]
    public async Task A_global_assignment_does_not_satisfy_a_scoped_request()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { ScopeId = Guid.NewGuid() },
                CancellationToken.None));
        });
    }

    // -------------------------------------------------- 8. permission active

    [Fact]
    public async Task An_inactive_permission_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.True(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));

            await ExecuteAsync(
                "UPDATE permission SET is_active = false WHERE id = @id",
                fixture.PermissionId.Value);

            Assert.False(await IsAllowedAsync(service,
                fixture.Request(), CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_permission_the_role_does_not_carry_is_denied()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            Assert.False(await IsAllowedAsync(service,
                fixture.Request() with { PermissionCode = "some.other.permission" },
                CancellationToken.None));
        });
    }

    // ------------------------------------------------- 9. UR10 actor type

    /// <summary>
    /// UR10, the third edge. UR9 blocks the assignment and RP6 blocks the
    /// grant; this blocks the act. The agent row is inserted with raw SQL
    /// because the domain has no Agent factory — AU11 reserves the actor type
    /// and blocks its creation in V1 — yet the runtime check must still hold
    /// for the release that lifts that restriction.
    /// </summary>
    [Fact]
    public async Task An_agent_cannot_exercise_a_human_only_permission()
    {
        await RunAsync(
            requiresHumanActor: true,
            async (context, service, fixture) =>
            {
                var agentId = await SeedAgentHoldingRoleAsync(fixture);

                try
                {
                    // The human holder of the very same role is allowed, so the
                    // denial below is about the actor and nothing else.
                    Assert.True(await IsAllowedAsync(service,
                        fixture.Request(), CancellationToken.None));

                    Assert.False(await IsAllowedAsync(service,
                        fixture.Request() with { UserId = agentId },
                        CancellationToken.None));
                }
                finally
                {
                    await DeleteActorAsync(agentId);
                }
            });
    }

    [Fact]
    public async Task An_agent_may_exercise_a_permission_that_allows_non_humans()
    {
        await RunAsync(
            requiresHumanActor: false,
            async (context, service, fixture) =>
            {
                var agentId = await SeedAgentHoldingRoleAsync(fixture);

                try
                {
                    Assert.True(await IsAllowedAsync(service,
                        fixture.Request() with { UserId = agentId },
                        CancellationToken.None));
                }
                finally
                {
                    await DeleteActorAsync(agentId);
                }
            });
    }

    // ------------------------------------------- the deliberately absent one

    /// <summary>
    /// AUT-C5: deactivating a role "prevents NEW assignments" while "existing
    /// assignments are unaffected". This is a positive regression test, not
    /// merely the absence of a predicate — adding role.IsActive to the query
    /// because it looks safer would strip access from every current holder of
    /// a retired role.
    /// </summary>
    [Fact]
    public async Task A_deactivated_role_still_authorises_its_existing_holders()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            await ExecuteAsync(
                "UPDATE role SET is_active = false WHERE id = @id",
                fixture.RoleId.Value);

            Assert.True(
                await IsAllowedAsync(service,
                    fixture.Request(), CancellationToken.None),
                "Deactivating a role must not strand its existing holders.");
        });
    }

    // ------------------------------------------------------------- fixtures


    // ------------------------------------- which assignment is reported

    /// <summary>
    /// A single eligible assignment is reported, with the role's name as it
    /// stands, and the assignment id that closes AUD-O1.
    /// </summary>
    [Fact]
    public async Task An_authorised_act_names_the_assignment_that_permitted_it()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var result = await service.IsAllowedAsync(
                fixture.Request(), CancellationToken.None);

            Assert.True(result.IsAllowed);
            Assert.NotNull(result.Authority);
            Assert.Equal(fixture.AssignmentId, result.Authority!.AssignmentId);
            Assert.Equal(fixture.RoleId, result.Authority.RoleId);
            Assert.Equal(ScopeType.Global, result.Authority.ScopeType);
            Assert.Null(result.Authority.ScopeId);
            Assert.False(string.IsNullOrWhiteSpace(result.Authority.RoleName));
        });
    }

    /// <summary>
    /// THE POINT OF THE TIE-BREAK. Holding two roles that both carry a
    /// permission is an ordinary configuration, not a conflict, so the
    /// DECISION must be unchanged — still allowed — while exactly one
    /// assignment is reported. Earliest EffectiveFrom wins.
    ///
    /// The second assignment is seeded with a LATER EffectiveFrom, so a naive
    /// implementation returning whichever row the planner reached first would
    /// fail this intermittently rather than never.
    /// </summary>
    [Fact]
    public async Task The_earliest_effective_assignment_is_the_one_reported()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var later = await SeedSecondAssignmentAsync(
                context, fixture, fixture.EffectiveFrom.AddHours(1));

            try
            {
                var result = await service.IsAllowedAsync(
                    fixture.Request(), CancellationToken.None);

                Assert.True(result.IsAllowed);

                Assert.Equal(
                    fixture.AssignmentId, result.Authority!.AssignmentId);

                Assert.NotEqual(later.AssignmentId, result.Authority.AssignmentId);
            }
            finally
            {
                await RemoveRoleAsync(later.RoleId);
            }
        });
    }

    /// <summary>
    /// Determinism is the property REV-Q6 depends on: asking again must give
    /// the same answer, or a point-in-time reconstruction would disagree with
    /// the record it is reconstructing.
    /// </summary>
    [Fact]
    public async Task The_reported_assignment_is_stable_across_repeated_asks()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var other = await SeedSecondAssignmentAsync(
                context, fixture, fixture.EffectiveFrom);

            try
            {
                var first = await service.IsAllowedAsync(
                    fixture.Request(), CancellationToken.None);

                var second = await service.IsAllowedAsync(
                    fixture.Request(), CancellationToken.None);

                // Same EffectiveFrom on both, so the assignment id breaks the
                // tie — and must break it the same way twice.
                Assert.Equal(
                    first.Authority!.AssignmentId,
                    second.Authority!.AssignmentId);
            }
            finally
            {
                await RemoveRoleAsync(other.RoleId);
            }
        });
    }

    [Fact]
    public async Task A_refused_act_reports_no_assignment()
    {
        await RunAsync(async (context, service, fixture) =>
        {
            var result = await service.IsAllowedAsync(
                fixture.Request() with { UserId = UserId.New() },
                CancellationToken.None);

            Assert.False(result.IsAllowed);
            Assert.Null(result.Authority);
        });
    }

    /// <summary>
    /// A second role granting the same permission to the same user. Created
    /// through the domain so the assignment is a real one, subject to the same
    /// constraints as any other.
    /// </summary>
    private static async Task<(UserRoleId AssignmentId, RoleId RoleId)>
        SeedSecondAssignmentAsync(
        LigatureDbContext context,
        Fixture fixture,
        DateTimeOffset effectiveFrom)
    {
        var discriminator = Guid.NewGuid().ToString("N");

        var role = Role.Create(
            RoleId.New(),
            $"Second Authorization Role {discriminator[..8]}",
            $"authz-role2-{discriminator[..12]}",
            "Created by AuthorizationServiceTests.",
            isSystemRole: false,
            Now,
            User.SystemUserId);

        var grant = RolePermission.Create(
            RolePermissionId.New(),
            role.Id,
            fixture.PermissionId,
            Now,
            User.SystemUserId);

        var assignment = UserRole.Create(
            UserRoleId.New(),
            fixture.UserId,
            ActorType.Human,
            role.Id,
            ScopeType.Global,
            scopeId: null,
            effectiveFrom: effectiveFrom,
            effectiveTo: null,
            assignedAt: Now,
            assignedBy: User.SystemUserId,
            assignmentReason: "Second assignment for selection tests.",
            createdAt: Now,
            createdBy: User.SystemUserId);

        context.AddRange(role, grant, assignment);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        return (assignment.Id, role.Id);
    }

    /// <summary>
    /// The fixture's own teardown deletes role_permission by ITS role id and
    /// then the permission, so a second role granting the same permission
    /// leaves a row that makes the permission undeletable. Removed here rather
    /// than by widening CleanUpAsync, which would then have to know about rows
    /// only two tests create.
    /// </summary>
    private static async Task RemoveRoleAsync(RoleId roleId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_role WHERE role_id = @role",
            "DELETE FROM role_permission WHERE role_id = @role",
            "DELETE FROM role WHERE id = @role",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("role", roleId.Value);

            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record Fixture(
        UserId UserId,
        RoleId RoleId,
        PermissionId PermissionId,
        RolePermissionId GrantId,
        UserRoleId AssignmentId,
        string PermissionCode,
        DateTimeOffset EffectiveFrom)
    {
        public AuthorizationRequest Request()
            => new(UserId, PermissionCode, Now, "Global", null);
    }

    private static async Task RunAsync(
        Func<LigatureDbContext, AuthorizationService, Fixture, Task> body)
        => await RunAsync(requiresHumanActor: false, body);

    private static async Task RunAsync(
        bool requiresHumanActor,
        Func<LigatureDbContext, AuthorizationService, Fixture, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var context = CreateContext();
        var service = new AuthorizationService(context);

        var fixture = await SeedAsync(context, requiresHumanActor);

        try
        {
            await body(context, service, fixture);
        }
        finally
        {
            await CleanUpAsync(fixture);
        }
    }

    private static async Task<Fixture> SeedAsync(
        LigatureDbContext context,
        bool requiresHumanActor)
    {
        var discriminator = Guid.NewGuid().ToString("N");
        var effectiveFrom = Now.AddDays(-1);

        var user = User.CreateHuman(
            UserId.New(),
            "Auth",
            "Subject",
            $"Auth Subject {discriminator[..8]}",
            $"authz-{discriminator}@example.test",
            Now,
            User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            $"authz-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        var role = Role.Create(
            RoleId.New(),
            $"Authorization Test Role {discriminator[..8]}",
            $"authz-role-{discriminator[..12]}",
            "Created by AuthorizationServiceTests.",
            isSystemRole: false,
            Now,
            User.SystemUserId);

        var permissionCode = $"authz.test.{discriminator[..12]}";

        var permission = Permission.Create(
            PermissionId.New(),
            permissionCode,
            "Authorization Test Permission",
            "Created by AuthorizationServiceTests.",
            "AuthzTest",
            "Execute",
            requiresHumanActor,
            Now,
            User.SystemUserId);

        var grant = RolePermission.Create(
            RolePermissionId.New(),
            role.Id,
            permission.Id,
            Now,
            User.SystemUserId);

        var assignment = UserRole.Create(
            UserRoleId.New(),
            user.Id,
            ActorType.Human,
            role.Id,
            ScopeType.Global,
            scopeId: null,
            effectiveFrom: effectiveFrom,
            effectiveTo: null,
            assignedAt: Now,
            assignedBy: User.SystemUserId,
            assignmentReason: "Authorization service tests.",
            createdAt: Now,
            createdBy: User.SystemUserId);

        context.AddRange(user, identity, role, permission, grant, assignment);
        await context.SaveChangesAsync(CancellationToken.None);

        // The service reads with AsNoTracking, but the fixture entities are
        // tracked; detaching keeps later raw-SQL edits visible to it.
        context.ChangeTracker.Clear();

        return new Fixture(
            user.Id, role.Id, permission.Id, grant.Id, assignment.Id,
            permissionCode, effectiveFrom);
    }

    /// <summary>
    /// Seeds an agent actor holding the fixture's role, entirely in raw SQL.
    ///
    /// Two reasons it cannot go through the domain or through mutation. The
    /// domain has no Agent factory — AU11 reserves the actor type and blocks
    /// its creation in V1 — yet UR10 must already hold for the release that
    /// lifts that restriction. And ActorType cannot be changed in place at all:
    /// the composite FK (UserId, ActorType) is exactly what makes the actor-type
    /// rules declarative, so it refuses to let a user's kind drift out from
    /// under its identities and assignments. Immutability enforced, as designed.
    /// </summary>
    private static async Task<UserId> SeedAgentHoldingRoleAsync(Fixture fixture)
    {
        var agentId = UserId.New();
        var identityId = UserIdentityId.New();
        var discriminator = Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await using (var actor = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email,
                 status, created_at, created_by, updated_at, updated_by)
            VALUES
                (@id, 'Agent', NULL, NULL, @displayName, NULL,
                 'Active', @now, @system, @now, @system)
            """, connection, transaction))
        {
            actor.Parameters.AddWithValue("id", agentId.Value);
            actor.Parameters.AddWithValue("displayName", $"Agent {discriminator[..8]}");
            actor.Parameters.AddWithValue("now", Now);
            actor.Parameters.AddWithValue("system", User.SystemUserId.Value);
            await actor.ExecuteNonQueryAsync();
        }

        await using (var identity = new NpgsqlCommand(
            """
            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@id, @userId, 'Agent', 'Local', 'Application',
                 @subjectId, @username, 'Active', @now, @system)
            """, connection, transaction))
        {
            identity.Parameters.AddWithValue("id", identityId.Value);
            identity.Parameters.AddWithValue("userId", agentId.Value);
            identity.Parameters.AddWithValue("subjectId", identityId.Value.ToString());
            identity.Parameters.AddWithValue("username", $"agent-{discriminator[..12]}");
            identity.Parameters.AddWithValue("now", Now);
            identity.Parameters.AddWithValue("system", User.SystemUserId.Value);
            await identity.ExecuteNonQueryAsync();
        }

        await using (var assignment = new NpgsqlCommand(
            """
            INSERT INTO user_role
                (id, user_id, actor_type, role_id, scope_type, scope_id,
                 effective_from, effective_to, assigned_at, assigned_by,
                 assignment_reason)
            VALUES
                (@id, @userId, 'Agent', @roleId, 'Global', NULL,
                 @from, @to, @now, @system, 'Authorization service tests.')
            """, connection, transaction))
        {
            assignment.Parameters.AddWithValue("id", Guid.NewGuid());
            assignment.Parameters.AddWithValue("userId", agentId.Value);
            assignment.Parameters.AddWithValue("roleId", fixture.RoleId.Value);
            assignment.Parameters.AddWithValue("from", fixture.EffectiveFrom);
            // Agent assignments must carry a finite end date (UR8).
            assignment.Parameters.AddWithValue("to", Now.AddYears(1));
            assignment.Parameters.AddWithValue("now", Now);
            assignment.Parameters.AddWithValue("system", User.SystemUserId.Value);
            await assignment.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return agentId;
    }

    private static async Task ExecuteAsync(
        string sql,
        Guid id,
        DateTimeOffset? value = null,
        (Guid Actor, object? Future)? extra = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("id", id);

        if (value is not null)
            command.Parameters.AddWithValue("value", value.Value);

        if (extra is not null)
        {
            command.Parameters.AddWithValue("actor", extra.Value.Actor);

            if (extra.Value.Future is not null)
                command.Parameters.AddWithValue("future", extra.Value.Future);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// An active human with an active identity and no role assignment at all.
    /// </summary>
    private static async Task<UserId> SeedBystanderAsync(LigatureDbContext context)
    {
        var discriminator = Guid.NewGuid().ToString("N");

        var bystander = User.CreateHuman(
            UserId.New(),
            "By",
            "Stander",
            $"By Stander {discriminator[..8]}",
            $"bystander-{discriminator}@example.test",
            Now,
            User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            bystander.Id,
            ActorType.Human,
            $"bystander-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        context.AddRange(bystander, identity);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        return bystander.Id;
    }

    private static async Task DeleteActorAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_role WHERE user_id = @user",
            "DELETE FROM user_identity WHERE user_id = @user",
            "DELETE FROM app_user WHERE id = @user",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user", userId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CleanUpAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var statements = new[]
        {
            ("DELETE FROM user_role WHERE user_id = @user", "user"),
            ("DELETE FROM user_identity WHERE user_id = @user", "user"),
            ("DELETE FROM role_permission WHERE role_id = @role", "role"),
            ("DELETE FROM permission WHERE id = @permission", "permission"),
            ("DELETE FROM role WHERE id = @role", "role"),
            ("DELETE FROM app_user WHERE id = @user", "user"),
        };

        foreach (var (sql, parameter) in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue(parameter, parameter switch
            {
                "user" => fixture.UserId.Value,
                "role" => fixture.RoleId.Value,
                _ => fixture.PermissionId.Value,
            });

            await command.ExecuteNonQueryAsync();
        }
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

    private static async Task<bool> IsProvisionedAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE actor_type = 'System'",
            connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static string ConnectionString => TestDatabase.ConnectionString;


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    /// <summary>
    /// Every assertion in this class is about the DECISION, which is what they
    /// have always asserted and what must not change: the service now also
    /// reports WHICH assignment decided, and that selection is tested
    /// separately in the selection tests above.
    ///
    /// Routing them through here rather than editing each call keeps the
    /// regression value intact — including the deliberate role.IsActive
    /// omission that one of these tests exists to pin.
    /// </summary>
    private static async Task<bool> IsAllowedAsync(
        IAuthorizationService service,
        AuthorizationRequest request,
        CancellationToken cancellationToken)
        => (await service.IsAllowedAsync(request, cancellationToken)).IsAllowed;
}
