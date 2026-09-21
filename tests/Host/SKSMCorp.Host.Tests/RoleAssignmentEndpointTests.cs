using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// AUT-C1 / AUT-C2 over HTTP (docs/requirements.md, "Role Assignment").
///
/// What the endpoints themselves own: the shapes (201 with exactly
/// { userRoleAssignmentId } and no Location; 204 with no body), binding
/// refusals, and the command refusals mapped onto the established surface.
/// The rules are proven by the integration suite; one example of each refusal
/// class is enough here.
///
/// Callers are permanent, with identifiers of their own.
/// </summary>
public sealed class RoleAssignmentEndpointTests
{
    private const string Password = "correct-horse-battery-staple";
    private const string OverlapRefusal =
        "The user already holds this role for this scope in an overlapping period.";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("9a000000-0000-4000-8000-000000000001"),
         Guid.Parse("9a000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9a000000-0000-4000-8000-000000000011"),
         Guid.Parse("9a000000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task A_grant_is_created_with_exactly_its_identifier_and_no_location()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            var response = await GrantAsync(client, admin, target, new { RoleId = roleId, Reason = "Onboarding." });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(
                ["userRoleAssignmentId"],
                body.RootElement.EnumerateObject().Select(x => x.Name));

            var id = body.RootElement.GetProperty("userRoleAssignmentId").GetGuid();

            Assert.Equal(1, await ScalarAsync(
                "SELECT count(*) FROM user_role WHERE id = @id AND user_id = @user",
                ("id", id), ("user", target)));
        });
    }

    /// <summary>Dates travel as ISO 8601 and are stored as sent.</summary>
    [Fact]
    public async Task A_future_grant_with_an_end_is_stored_as_sent()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            var from = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(14), TimeSpan.Zero);
            var to = from.AddMonths(2);

            var response = await GrantAsync(client, admin, target,
                new { RoleId = roleId, EffectiveFrom = from, EffectiveTo = to, Reason = "Starts next fortnight." });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            Assert.Equal(1, await ScalarAsync(
                "SELECT count(*) FROM user_role WHERE user_id = @user AND effective_from = @from AND effective_to = @to",
                ("user", target), ("from", from), ("to", to)));
        });
    }

    [Fact]
    public async Task A_revocation_is_no_content_and_ends_the_assignment()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            var id = await GrantedIdAsync(client, admin, target, roleId);

            var response = await RevokeAsync(client, admin, id, new { Reason = "Left the team." });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            Assert.Equal(1, await ScalarAsync(
                "SELECT count(*) FROM user_role WHERE id = @id AND revoked_at IS NOT NULL AND effective_to IS NOT NULL",
                ("id", id)));
        });
    }

    [Fact]
    public async Task No_carrier_is_401_for_both()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            var id = await GrantedIdAsync(client, admin, target, roleId);

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await GrantAsync(client, null, target, new { RoleId = roleId, Reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await RevokeAsync(client, null, id, new { Reason = "x" })).StatusCode);

            Assert.Equal(1, await AssignmentsAsync(target));
        });
    }

    /// <summary>user-administrator holds user.* but not role.grant or role.revoke.</summary>
    [Fact]
    public async Task A_caller_without_the_permission_is_refused_with_the_error_body()
    {
        await RunAsync(async (client, admin, userAdmin, target, roleId) =>
        {
            await AssertErrorAsync(await GrantAsync(client, userAdmin, target, new { RoleId = roleId, Reason = "x" }));

            var id = await GrantedIdAsync(client, admin, target, roleId);

            await AssertErrorAsync(await RevokeAsync(client, userAdmin, id, new { Reason = "x" }));

            Assert.Equal(1, await AssignmentsAsync(target));
        });
    }

    /// <summary>A missing required field is refused where the request is bound.</summary>
    [Fact]
    public async Task A_missing_role_or_reason_is_refused()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            await AssertErrorAsync(await GrantAsync(client, admin, target, new { Reason = "No role." }));
            await AssertErrorAsync(await GrantAsync(client, admin, target, new { RoleId = roleId }));
            await AssertErrorAsync(await GrantAsync(client, admin, target, new { RoleId = roleId, Reason = "  " }));

            Assert.Equal(0, await AssignmentsAsync(target));

            var id = await GrantedIdAsync(client, admin, target, roleId);

            await AssertErrorAsync(await RevokeAsync(client, admin, id, new { }));
            await AssertErrorAsync(await RevokeAsync(client, admin, id, new { Reason = "  " }));
        });
    }

    [Fact]
    public async Task An_overlapping_grant_is_refused_with_the_overlap_message()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            await GrantedIdAsync(client, admin, target, roleId);

            var response = await GrantAsync(client, admin, target, new { RoleId = roleId, Reason = "Again." });

            Assert.Equal(OverlapRefusal, await AssertErrorAsync(response));
            Assert.Equal(1, await AssignmentsAsync(target));
        });
    }

    [Fact]
    public async Task A_past_start_and_an_empty_period_are_refused()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            var from = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(14), TimeSpan.Zero);

            await AssertErrorAsync(await GrantAsync(client, admin, target,
                new { RoleId = roleId, EffectiveFrom = DateTimeOffset.UtcNow.AddHours(-1), Reason = "Backdated." }));
            await AssertErrorAsync(await GrantAsync(client, admin, target,
                new { RoleId = roleId, EffectiveFrom = from, EffectiveTo = from, Reason = "Empty." }));

            Assert.Equal(0, await AssignmentsAsync(target));
        });
    }

    [Fact]
    public async Task Revoking_an_unknown_or_already_revoked_assignment_is_refused()
    {
        await RunAsync(async (client, admin, _, target, roleId) =>
        {
            await AssertErrorAsync(await RevokeAsync(client, admin, Guid.NewGuid(), new { Reason = "Nothing there." }));

            var id = await GrantedIdAsync(client, admin, target, roleId);

            Assert.Equal(HttpStatusCode.NoContent, (await RevokeAsync(client, admin, id, new { Reason = "First." })).StatusCode);

            await AssertErrorAsync(await RevokeAsync(client, admin, id, new { Reason = "Second." }));
        });
    }

    [Fact]
    public async Task Both_endpoints_appear_in_the_api_document()
    {
        await using var factory = new HostFactory("true");

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        foreach (var (route, permission) in new[]
                 {
                     ("/api/users/{userId}/role-assignments", "role.grant"),
                     ("/api/role-assignments/{assignmentId}/revoke", "role.revoke"),
                 })
        {
            Assert.True(paths.TryGetProperty(route, out var path), $"The API document does not describe {route}.");

            var description = path.GetProperty("post").GetProperty("description").GetString() ?? "";

            Assert.Contains(permission, description, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------ harness

    private static async Task RunAsync(Func<HttpClient, string, string, Guid, Guid, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, Administrator, "role-endpoint-administrator", "security-administrator");
        var userAdmin = await EnsureCallerAsync(client, UserAdministrator, "role-endpoint-user-administrator", "user-administrator");

        var target = await SeedTargetAsync();
        var roleId = await RoleIdAsync("access-reviewer");

        try
        {
            await body(client, admin, userAdmin, target, roleId);
        }
        finally
        {
            await DeleteTargetAsync(target);
        }
    }

    private static async Task<string> AssertErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var error = body.RootElement.GetProperty("error").GetString();

        Assert.False(string.IsNullOrWhiteSpace(error), "A refusal must carry the host's error body.");

        return error!;
    }

    private static async Task<Guid> GrantedIdAsync(HttpClient client, string admin, Guid target, Guid roleId)
    {
        var response = await GrantAsync(client, admin, target, new { RoleId = roleId, Reason = "Granted for the test." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("userRoleAssignmentId").GetGuid();
    }

    private static Task<HttpResponseMessage> GrantAsync(HttpClient client, string? carrier, Guid target, object payload)
        => PostAsync(client, carrier, $"/api/users/{target}/role-assignments", payload);

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, string? carrier, Guid assignment, object payload)
        => PostAsync(client, carrier, $"/api/role-assignments/{assignment}/revoke", payload);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        var username = $"permanent-{label}";
        var userId = new UserId(identifiers.User);

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == userId))
                await SeedCallerAsync(client, identifiers, label, username, roleCode);
        }

        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task SeedCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string username, string roleCode)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(identifiers.User), "Permanent", "Caller", $"Permanent {label}",
            $"permanent-{label}@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(identifiers.Identity), user.Id, ActorType.Human, username, now, User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash, now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, identity, token);

            var roleId = await context.Set<Role>().Where(x => x.Code == roleCode).Select(x => x.Id).SingleAsync();

            context.Add(
                UserRole.Create(
                    UserRoleId.New(), user.Id, ActorType.Human, roleId,
                    ScopeType.Global, scopeId: null,
                    effectiveFrom: now, effectiveTo: null,
                    assignedAt: now, assignedBy: User.SystemUserId,
                    assignmentReason: "Role assignment endpoint tests",
                    createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>An active human with an active local identity and no role.</summary>
    private static async Task<Guid> SeedTargetAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Role', 'Endpoint', 'Role Endpoint {unique[..8]}',
                  'role-endpoint-{unique}@example.test', 'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'role-endpoint-{unique[..16]}', 'Active', now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return userId;
    }

    private static async Task<Guid> RoleIdAsync(string code)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM role WHERE code = @code", connection);

        command.Parameters.AddWithValue("code", code);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> AssignmentsAsync(Guid target)
        => ScalarAsync("SELECT count(*) FROM user_role WHERE user_id = @user", ("user", target));

    private static async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    /// <summary>
    /// The target is only ever the SUBJECT of what these commands write, never
    /// an actor, and its assignments authorise no act, so it can be removed.
    /// </summary>
    private static async Task DeleteTargetAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_role WHERE user_id = @id",
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
