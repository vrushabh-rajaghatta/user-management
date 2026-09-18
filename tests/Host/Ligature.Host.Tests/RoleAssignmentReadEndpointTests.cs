using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// AUT-Q2 and the grantable-role list over HTTP (docs/requirements.md,
/// "AUT-Q2"): exact shapes, the includeInactive parameter, and who may read.
///
/// Three permanent callers, with identifiers of their own: a security
/// administrator (role.read, role.grant, role.revoke), an access reviewer
/// (role.read only: the read-only path), and a user administrator (no
/// role.read at all).
/// </summary>
public sealed class RoleAssignmentReadEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] RowMembers =
    [
        "assignedAt", "assignedBy", "assignmentId", "assignmentReason", "effectiveFrom", "effectiveTo",
        "revocationReason", "revokedAt", "revokedBy", "roleId", "roleName", "state",
    ];

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9b000000-0000-4000-8000-000000000001"), Guid.Parse("9b000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9b000000-0000-4000-8000-000000000011"), Guid.Parse("9b000000-0000-4000-8000-000000000012"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9b000000-0000-4000-8000-000000000021"), Guid.Parse("9b000000-0000-4000-8000-000000000022"));

    // ================================================================ AUT-Q2

    [Fact]
    public async Task The_response_is_exactly_assignments_and_each_row_exactly_its_fields()
    {
        await RunAsync(async (client, callers, target, roleId) =>
        {
            var granted = await GrantAsync(client, callers.Admin, target, roleId);

            using var body = await JsonAsync(await GetAsync(client, callers.Admin, $"/api/users/{target}/role-assignments"));

            Assert.Equal(["assignments"], body.RootElement.EnumerateObject().Select(x => x.Name));

            var row = Assert.Single(body.RootElement.GetProperty("assignments").EnumerateArray());

            Assert.Equal(RowMembers, row.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(granted, row.GetProperty("assignmentId").GetGuid());
            Assert.Equal(roleId, row.GetProperty("roleId").GetGuid());
            Assert.Equal("Access Reviewer", row.GetProperty("roleName").GetString());
            Assert.Equal("Active", row.GetProperty("state").GetString());
            Assert.Equal("Granted by the read tests.", row.GetProperty("assignmentReason").GetString());

            var by = row.GetProperty("assignedBy");
            Assert.Equal(["displayName", "userId"], by.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(SecurityAdministrator.User, by.GetProperty("userId").GetGuid());

            Assert.Equal(JsonValueKind.Null, row.GetProperty("effectiveTo").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("revokedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("revokedBy").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("revocationReason").ValueKind);
        });
    }

    /// <summary>Q4 — history is opt-in; the revoked future grant reads Revoked.</summary>
    [Fact]
    public async Task History_is_returned_only_with_include_inactive_true()
    {
        await RunAsync(async (client, callers, target, roleId) =>
        {
            var future = await GrantAsync(client, callers.Admin, target, roleId,
                effectiveFrom: new DateTimeOffset(DateTime.UtcNow.Date.AddDays(20), TimeSpan.Zero));

            Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, callers.Admin,
                $"/api/role-assignments/{future}/revoke", new { Reason = "Cancelled before it started." })).StatusCode);

            foreach (var query in new[] { "", "?includeInactive=false" })
            {
                using var current = await JsonAsync(await GetAsync(client, callers.Admin, $"/api/users/{target}/role-assignments{query}"));
                Assert.Empty(current.RootElement.GetProperty("assignments").EnumerateArray());
            }

            using var all = await JsonAsync(await GetAsync(client, callers.Admin, $"/api/users/{target}/role-assignments?includeInactive=true"));
            var row = Assert.Single(all.RootElement.GetProperty("assignments").EnumerateArray());

            Assert.Equal("Revoked", row.GetProperty("state").GetString());
            Assert.Equal(row.GetProperty("effectiveFrom").GetDateTimeOffset(), row.GetProperty("effectiveTo").GetDateTimeOffset());
            Assert.Equal(SecurityAdministrator.User, row.GetProperty("revokedBy").GetProperty("userId").GetGuid());
        });
    }

    [Theory]
    [InlineData("includeInactive=yes")]
    [InlineData("includeInactive=1")]
    [InlineData("includeInactive=")]
    [InlineData("includeInactive=true&includeInactive=false")]
    public async Task A_malformed_include_inactive_is_refused_with_the_error_body(string query)
    {
        await RunAsync(async (client, callers, target, _) =>
            await AssertErrorAsync(await GetAsync(client, callers.Admin, $"/api/users/{target}/role-assignments?{query}")));
    }

    /// <summary>D7's read-only path: role.read alone reads the assignments.</summary>
    [Fact]
    public async Task An_access_reviewer_can_read_the_assignments()
    {
        await RunAsync(async (client, callers, target, roleId) =>
        {
            await GrantAsync(client, callers.Admin, target, roleId);

            var response = await GetAsync(client, callers.Reviewer, $"/api/users/{target}/role-assignments");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    [Fact]
    public async Task A_caller_without_role_read_an_unknown_user_and_no_carrier_are_refused()
    {
        await RunAsync(async (client, callers, target, _) =>
        {
            await AssertErrorAsync(await GetAsync(client, callers.UserAdmin, $"/api/users/{target}/role-assignments"));
            await AssertErrorAsync(await GetAsync(client, callers.Admin, $"/api/users/{Guid.NewGuid()}/role-assignments"));

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, $"/api/users/{target}/role-assignments")).StatusCode);
        });
    }

    // ================================================================ roles

    [Fact]
    public async Task The_role_list_is_exactly_the_active_roles_with_exactly_their_fields()
    {
        await RunAsync(async (client, callers, _, _) =>
        {
            using var body = await JsonAsync(await GetAsync(client, callers.Reviewer, "/api/roles"));

            Assert.Equal(["roles"], body.RootElement.EnumerateObject().Select(x => x.Name));

            var roles = body.RootElement.GetProperty("roles").EnumerateArray().ToList();

            Assert.All(roles, role => Assert.Equal(
                ["description", "name", "roleId"],
                role.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)));

            Assert.Equal(
                (await ActiveRoleIdsAsync()).Order(),
                roles.Select(x => x.GetProperty("roleId").GetGuid()).Order());
        });
    }

    [Fact]
    public async Task The_role_list_refuses_a_caller_without_role_read_and_no_carrier()
    {
        await RunAsync(async (client, callers, _, _) =>
        {
            await AssertErrorAsync(await GetAsync(client, callers.UserAdmin, "/api/roles"));

            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, null, "/api/roles")).StatusCode);
        });
    }

    [Fact]
    public async Task Both_reads_appear_in_the_api_document_and_require_a_carrier()
    {
        await using var factory = new HostFactory("true");

        using var document = JsonDocument.Parse(
            await (await factory.CreateClient().GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        foreach (var (route, permission) in new[]
                 {
                     ("/api/users/{userId}/role-assignments", "role.read"),
                     ("/api/roles", "role.read"),
                 })
        {
            Assert.True(paths.TryGetProperty(route, out var path), $"The API document does not describe {route}.");
            Assert.True(path.TryGetProperty("get", out var get), $"The API document does not describe GET {route}.");

            Assert.Contains(permission, get.GetProperty("description").GetString() ?? "", StringComparison.Ordinal);
            Assert.Equal(2, get.GetProperty("security").GetArrayLength());
        }
    }

    // ================================================================ harness

    private sealed record Callers(string Admin, string Reviewer, string UserAdmin);

    private static async Task RunAsync(Func<HttpClient, Callers, Guid, Guid, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, SecurityAdministrator, "role-read-security-administrator", "security-administrator"),
            await EnsureCallerAsync(client, AccessReviewer, "role-read-access-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, UserAdministrator, "role-read-user-administrator", "user-administrator"));

        var target = await SeedTargetAsync();
        var roleId = await RoleIdAsync("access-reviewer");

        try
        {
            await body(client, callers, target, roleId);
        }
        finally
        {
            await DeleteTargetAsync(target);
        }
    }

    private static async Task<Guid> GrantAsync(
        HttpClient client, string carrier, Guid target, Guid roleId, DateTimeOffset? effectiveFrom = null)
    {
        var response = await PostAsync(client, carrier, $"/api/users/{target}/role-assignments",
            new { RoleId = roleId, EffectiveFrom = effectiveFrom, Reason = "Granted by the read tests." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = await JsonAsync(response);

        return body.RootElement.GetProperty("userRoleAssignmentId").GetGuid();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = await JsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string? carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        var username = $"permanent-{label}";

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(identifiers.User)))
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

            context.Add(UserRole.Create(
                UserRoleId.New(), user.Id, ActorType.Human, roleId, ScopeType.Global, scopeId: null,
                effectiveFrom: now, effectiveTo: null, assignedAt: now, assignedBy: User.SystemUserId,
                assignmentReason: "Role assignment read endpoint tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SeedTargetAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'Role', 'Read', 'Role Read {unique[..8]}', 'role-read-{unique}@example.test',
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'role-read-{unique[..16]}', 'Active', now(), '{system}');
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

    private static async Task<List<Guid>> ActiveRoleIdsAsync()
    {
        var ids = new List<Guid>();

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM role WHERE is_active", connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));

        return ids;
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

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
