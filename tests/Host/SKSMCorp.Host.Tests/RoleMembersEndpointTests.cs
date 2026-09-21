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
/// AUT-Q4 over HTTP (docs/requirements.md, "AUT-Q4 GetRoleMembers", RH-A7 to
/// RH-A10, and the route shape).
///
/// THE ROUTE IS WHAT IS UNDER TEST: its members, its parameters and its
/// refusals. The temporal semantics and the derivation of the count are held
/// to the contract in RoleMembersReadTests, which has a database of its own and
/// can seed roles and holdings; these tests use the seeded roles, which the
/// permanent callers already hold.
///
/// One half of RH-A7 cannot be asserted here. Every SEEDED role that holds
/// role.read also holds user.read, so a caller with role.read and no user.read
/// needs a tenant role — which this shared database will not have roles created
/// in. That half is proven in RoleMembersReadTests.
/// </summary>
public sealed class RoleMembersEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] MemberMembers =
    [
        "assignedAt", "assignedBy", "assignmentId", "assignmentReason", "displayName",
        "effectiveFrom", "effectiveTo", "email", "status", "userId",
    ];

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000111"), Guid.Parse("9c5e0000-0000-4000-8000-000000000112"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000121"), Guid.Parse("9c5e0000-0000-4000-8000-000000000122"));

    /// <summary>
    /// The shape: the three top-level members, and each holder with exactly
    /// its ten. The access-reviewer role is read because its permanent caller
    /// holds it, so the list is never empty.
    /// </summary>
    [Fact]
    public async Task The_members_of_a_role_are_answered_with_exactly_their_members()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, await PathAsync("access-reviewer"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(
                ["activeHolderCount", "asOf", "members"],
                body.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            var members = body.RootElement.GetProperty("members").EnumerateArray().ToList();

            Assert.NotEmpty(members);
            Assert.All(members, member => Assert.Equal(MemberMembers, Members(member)));

            // assignedBy is the administrator, named.
            var assignedBy = members[0].GetProperty("assignedBy");

            Assert.Equal(
                ["displayName", "userId"],
                assignedBy.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            // The count is served with the list, and agrees with it.
            var distinct = members
                .Select(x => x.GetProperty("userId").GetString())
                .Distinct()
                .Count();

            Assert.Equal(distinct, body.RootElement.GetProperty("activeHolderCount").GetInt32());
        });
    }

    /// <summary>
    /// The three fields that must not appear. Only active holdings are served,
    /// so a revocation field would be permanently null; and neither actor type
    /// nor scope is part of this answer (RH3).
    /// </summary>
    [Fact]
    public async Task No_member_carries_a_revocation_an_actor_type_or_a_scope()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, await PathAsync("access-reviewer"));

            using var body = await JsonAsync(response);

            foreach (var member in body.RootElement.GetProperty("members").EnumerateArray())
            {
                var names = Members(member);

                Assert.DoesNotContain("revokedAt", names);
                Assert.DoesNotContain("revokedBy", names);
                Assert.DoesNotContain("revocationReason", names);
                Assert.DoesNotContain("actorType", names);
                Assert.DoesNotContain("scopeId", names);
                Assert.DoesNotContain("scopeType", names);
            }
        });
    }

    /// <summary>RH-A8: an unknown role is 404, and the body is what says so.</summary>
    [Fact]
    public async Task An_unknown_role_is_not_found_with_its_sentence()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, $"/api/roles/{Guid.NewGuid()}/members");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal("The role does not exist.", body.RootElement.GetProperty("error").GetString());
        });
    }

    /// <summary>RH-A9: asOf must be one instant.</summary>
    [Theory]
    [InlineData("?asOf=yesterday")]
    [InlineData("?asOf=")]
    [InlineData("?asOf=2026-01-01T00:00:00Z&asOf=2026-02-01T00:00:00Z")]
    public async Task A_malformed_asOf_is_refused(string query)
    {
        await RunAsync(async (client, callers) =>
        {
            await AssertRefusedAsync(
                await GetAsync(client, callers.Reviewer, await PathAsync("access-reviewer") + query));
        });
    }

    /// <summary>RH-A9: a well-formed asOf is accepted, and echoed back.</summary>
    [Fact]
    public async Task A_well_formed_asOf_is_accepted_and_echoed()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(
                client, callers.Reviewer, await PathAsync("access-reviewer") + "?asOf=2030-01-01T00:00:00Z");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(
                new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                body.RootElement.GetProperty("asOf").GetDateTimeOffset());
        });
    }

    /// <summary>
    /// RH-A9 and RH3: the parameter is part of the frozen contract, and a
    /// VALUE names a scope that cannot exist. Accepting and ignoring it would
    /// be a claim to have filtered.
    /// </summary>
    [Fact]
    public async Task A_supplied_scopeId_is_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(
                client, callers.Reviewer, await PathAsync("access-reviewer") + $"?scopeId={Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Contains(
                "global",
                body.RootElement.GetProperty("error").GetString()!,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// RH-A7: user.read alone is not enough. The other direction needs a
    /// tenant role and is proven in RoleMembersReadTests.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_only_user_read_is_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.UserAdministrator, await PathAsync("access-reviewer"));

            await AssertRefusedAsync(response);

            using var body = await JsonAsync(response);
            var error = body.RootElement.GetProperty("error").GetString()!;

            Assert.DoesNotContain("role.read", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user.read", error, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>RH-A7: no carrier is 401, before the role is looked up.</summary>
    [Fact]
    public async Task Without_a_carrier_the_answer_is_unauthorized()
    {
        await RunAsync(async (client, _) =>
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, await PathAsync("access-reviewer"))).StatusCode);

            // Even for a role that does not exist: authentication comes first.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, $"/api/roles/{Guid.NewGuid()}/members")).StatusCode);
        });
    }

    // ============================================================== harness

    private static async Task<string> PathAsync(string roleCode)
        => $"/api/roles/{await RoleIdAsync(roleCode)}/members";

    private static string[] Members(JsonElement element)
        => [.. element.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal)];

    private sealed record Callers(string Reviewer, string UserAdministrator);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, AccessReviewer, "aut-q4-access-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, UserAdministrator, "aut-q4-user-administrator", "user-administrator"));

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
                assignmentReason: "Role member read tests", createdAt: now, createdBy: User.SystemUserId));

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
