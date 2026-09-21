using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// The role administration reads over HTTP (docs/requirements.md, "Role
/// administration read", RA-A11 and RA-A12, and the route shapes).
///
/// Three permanent callers: a security administrator and an access reviewer,
/// who both hold role.read, and a user administrator, who does not.
///
/// The deeper semantics — the counts, the derivation and asOf — are held to the
/// contract in RoleAdministrationReadTests, which can seed roles. These tests
/// are about the routes: their members, their parameters and their refusals.
/// </summary>
public sealed class RoleAdministrationEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private const string Administration = "/api/roles/administration";

    private const string Catalogue = "/api/permissions";

    private static readonly string[] RoleMembers =
    [
        "activeHolderCount", "agentAssignable", "code", "description", "isActive",
        "isSystemRole", "name", "permissionCount", "roleId",
    ];

    private static readonly string[] GrantMembers =
    [
        "action", "code", "grantedAt", "name", "permissionId", "requiresHumanActor",
        "resource", "revokedAt", "rolePermissionId",
    ];

    private static readonly string[] PermissionMembers =
    [
        "action", "code", "isActive", "name", "permissionId", "requiresHumanActor", "resource",
    ];

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000001"), Guid.Parse("9c5e0000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000011"), Guid.Parse("9c5e0000-0000-4000-8000-000000000012"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000021"), Guid.Parse("9c5e0000-0000-4000-8000-000000000022"));

    // =============================================================== AUT-Q5

    [Fact]
    public async Task The_administration_list_answers_every_role_with_exactly_its_members()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Administration);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);
            var roles = body.RootElement.GetProperty("roles").EnumerateArray().ToList();

            Assert.All(roles, role => Assert.Equal(RoleMembers, Members(role)));

            var codes = roles.Select(x => x.GetProperty("code").GetString()).ToList();

            Assert.Contains("user-administrator", codes);
            Assert.Contains("security-administrator", codes);
            Assert.Contains("access-reviewer", codes);

            var names = roles.Select(x => x.GetProperty("name").GetString()).ToList();
            Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), names);

            // The seeded roles are system roles, each with grants and at least
            // this test's own callers holding them.
            var reviewer = roles.Single(x => x.GetProperty("code").GetString() == "access-reviewer");

            Assert.True(reviewer.GetProperty("isSystemRole").GetBoolean());
            Assert.True(reviewer.GetProperty("isActive").GetBoolean());
            Assert.True(reviewer.GetProperty("permissionCount").GetInt32() > 0);
            Assert.True(reviewer.GetProperty("activeHolderCount").GetInt32() > 0);
            Assert.True(reviewer.GetProperty("agentAssignable").GetBoolean());
            Assert.False(
                roles.Single(x => x.GetProperty("code").GetString() == "user-administrator")
                    .GetProperty("agentAssignable").GetBoolean());
        });
    }

    [Fact]
    public async Task The_administration_list_accepts_both_parameters()
    {
        await RunAsync(async (client, callers) =>
        {
            foreach (var query in new[]
            {
                "?includeInactive=true",
                "?includeInactive=false",
                "?agentAssignableOnly=true",
                "?includeInactive=true&agentAssignableOnly=true",
            })
            {
                Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.Reviewer, Administration + query)).StatusCode);
            }
        });
    }

    /// <summary>RA-A12.</summary>
    [Fact]
    public async Task Malformed_parameters_are_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            foreach (var path in new[]
            {
                $"{Administration}?includeInactive=yes",
                $"{Administration}?includeInactive=true&includeInactive=false",
                $"{Administration}?agentAssignableOnly=1",
                $"/api/roles/{await RoleIdAsync("access-reviewer")}/permissions?asOf=yesterday",
                $"{Catalogue}?requiresHumanActor=maybe",
            })
            {
                await AssertRefusedAsync(await GetAsync(client, callers.Reviewer, path));
            }
        });
    }

    // =============================================================== AUT-Q3

    [Fact]
    public async Task A_roles_permissions_are_answered_with_exactly_their_members()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, $"/api/roles/{await RoleIdAsync("user-administrator")}/permissions");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);
            var grants = body.RootElement.GetProperty("permissions").EnumerateArray().ToList();

            Assert.NotEmpty(grants);
            Assert.All(grants, grant => Assert.Equal(GrantMembers, Members(grant)));

            var codes = grants.Select(x => x.GetProperty("code").GetString()).ToList();

            Assert.Equal(codes.OrderBy(x => x, StringComparer.Ordinal), codes);
            Assert.Contains("user.create", codes);
            Assert.All(grants, grant => Assert.Equal(JsonValueKind.Null, grant.GetProperty("revokedAt").ValueKind));
        });
    }

    /// <summary>
    /// RA-A8 and RA11: 404 with the sentence. The body is what distinguishes
    /// this from the 404 of a route that does not exist.
    /// </summary>
    [Fact]
    public async Task An_unknown_role_is_not_found()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, $"/api/roles/{Guid.NewGuid()}/permissions");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal("The role does not exist.", body.RootElement.GetProperty("error").GetString());
        });
    }

    // =============================================================== AUT-Q6

    [Fact]
    public async Task The_catalogue_answers_every_permission_and_filters()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Catalogue);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);
            var permissions = body.RootElement.GetProperty("permissions").EnumerateArray().ToList();

            Assert.All(permissions, permission => Assert.Equal(PermissionMembers, Members(permission)));

            var codes = permissions.Select(x => x.GetProperty("code").GetString()).ToList();

            Assert.Equal(codes.OrderBy(x => x, StringComparer.Ordinal), codes);
            Assert.Contains("role.manage", codes);

            using var filtered = await JsonAsync(await GetAsync(client, callers.Reviewer, $"{Catalogue}?resource=Session&requiresHumanActor=false"));
            var narrowed = filtered.RootElement.GetProperty("permissions").EnumerateArray().ToList();

            Assert.NotEmpty(narrowed);
            Assert.All(narrowed, permission =>
            {
                Assert.Equal("Session", permission.GetProperty("resource").GetString());
                Assert.False(permission.GetProperty("requiresHumanActor").GetBoolean());
            });
        });
    }

    // ============================================================== RA-A11

    [Fact]
    public async Task Every_read_requires_role_read_and_a_carrier()
    {
        await RunAsync(async (client, callers) =>
        {
            var roleId = await RoleIdAsync("access-reviewer");

            foreach (var path in new[] { Administration, $"/api/roles/{roleId}/permissions", Catalogue })
            {
                Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.Security, path)).StatusCode);

                await AssertRefusedAsync(await GetAsync(client, callers.UserAdmin, path));

                Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, carrier: null, path)).StatusCode);
            }
        });
    }

    /// <summary>RA6: the assignment workflow's own endpoint is untouched.</summary>
    [Fact]
    public async Task The_grantable_role_list_still_answers_its_three_members()
    {
        await RunAsync(async (client, callers) =>
        {
            using var body = await JsonAsync(await GetAsync(client, callers.Reviewer, "/api/roles"));

            Assert.All(
                body.RootElement.GetProperty("roles").EnumerateArray(),
                role => Assert.Equal(["description", "name", "roleId"], Members(role)));
        });
    }

    // ============================================================== harness

    private sealed record Callers(string Security, string Reviewer, string UserAdmin);

    private static IEnumerable<string> Members(JsonElement element)
        => element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, SecurityAdministrator, "aut-q5-security-administrator", "security-administrator"),
            await EnsureCallerAsync(client, AccessReviewer, "aut-q5-access-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, UserAdministrator, "aut-q5-user-administrator", "user-administrator"));

        await body(client, callers);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
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

    private static async Task<Guid> RoleIdAsync(string code)
    {
        await using var context = CreateContext();

        return (await context.Set<Role>().Where(x => x.Code == code).Select(x => x.Id).SingleAsync()).Value;
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
                assignmentReason: "Role administration read tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);
}
