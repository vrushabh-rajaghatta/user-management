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
/// AUT-Q7 over HTTP (docs/requirements.md, "AUT-Q7 WhoCanDo", RW-A9 to RW-A12,
/// and the route shape).
///
/// THE ROUTE IS WHAT IS UNDER TEST: its members, its parameters and its three
/// distinguishable empty-ish answers. The temporal semantics and the agreement
/// between the three views are held to the contract in WhoCanDoTests, which has
/// a database of its own and can seed catalogue entries.
///
/// As with AUT-Q4, one half of the AND cannot be asserted here: every SEEDED
/// role holding role.read also holds user.read.
/// </summary>
public sealed class WhoCanDoEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] HolderMembers =
        ["displayName", "email", "roles", "status", "userId"];

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000211"), Guid.Parse("9c5e0000-0000-4000-8000-000000000212"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000221"), Guid.Parse("9c5e0000-0000-4000-8000-000000000222"));

    /// <summary>
    /// The shape. role.read is read because the permanent callers hold roles
    /// carrying it, so the list is never empty.
    /// </summary>
    [Fact]
    public async Task The_holders_of_a_permission_are_answered_with_exactly_their_members()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Path("role.read"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(
                ["asOf", "holders", "permission"],
                body.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            var permission = body.RootElement.GetProperty("permission");

            Assert.Equal(
                ["code", "isActive", "name", "permissionId"],
                permission.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

            Assert.Equal("role.read", permission.GetProperty("code").GetString());
            Assert.True(permission.GetProperty("isActive").GetBoolean());

            var holders = body.RootElement.GetProperty("holders").EnumerateArray().ToList();

            Assert.NotEmpty(holders);
            Assert.All(holders, holder => Assert.Equal(HolderMembers, Members(holder)));

            // Every holder names at least one authorising role, which is the
            // "and why?" half of the question.
            Assert.All(holders, holder =>
            {
                var roles = holder.GetProperty("roles").EnumerateArray().ToList();

                Assert.NotEmpty(roles);
                Assert.All(roles, role => Assert.Equal(
                    ["name", "roleId"],
                    role.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal)));
            });

            // ONE ROW PER USER (RW5): no user appears twice, however many roles
            // carry the permission.
            var ids = holders.Select(x => x.GetProperty("userId").GetString()).ToList();

            Assert.Equal(ids.Distinct().Count(), ids.Count);
        });
    }

    /// <summary>
    /// RW-A10, the three answers a caller must be able to tell apart. This is
    /// the whole reason the permission is echoed with its isActive.
    /// </summary>
    [Fact]
    public async Task Unknown_retired_and_unheld_are_three_distinguishable_answers()
    {
        await RunAsync(async (client, callers) =>
        {
            // 1. Unknown: 404 with its sentence.
            var unknown = await GetAsync(client, callers.Reviewer, Path("no.such.permission"));

            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            using (var body = await JsonAsync(unknown))
            {
                Assert.Equal(
                    "The permission does not exist.",
                    body.RootElement.GetProperty("error").GetString());
            }

            // 2. Live but unheld: 200, isActive true, empty.
            var unheld = await GetAsync(client, callers.Reviewer, Path(await UnheldCodeAsync()));

            Assert.Equal(HttpStatusCode.OK, unheld.StatusCode);

            using (var body = await JsonAsync(unheld))
            {
                Assert.True(body.RootElement.GetProperty("permission").GetProperty("isActive").GetBoolean());
                Assert.Empty(body.RootElement.GetProperty("holders").EnumerateArray());
            }
        });
    }

    /// <summary>RW-A11: asOf must be one instant, and it is echoed.</summary>
    [Theory]
    [InlineData("?asOf=yesterday")]
    [InlineData("?asOf=")]
    [InlineData("?asOf=2026-01-01T00:00:00Z&asOf=2026-02-01T00:00:00Z")]
    public async Task A_malformed_asOf_is_refused(string query)
    {
        await RunAsync(async (client, callers) =>
        {
            await AssertRefusedAsync(await GetAsync(client, callers.Reviewer, Path("role.read") + query));
        });
    }

    [Fact]
    public async Task A_well_formed_asOf_is_accepted_and_echoed()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(
                client, callers.Reviewer, Path("role.read") + "?asOf=2030-01-01T00:00:00Z");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(
                new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                body.RootElement.GetProperty("asOf").GetDateTimeOffset());
        });
    }

    /// <summary>RW-A11: the scope parameters are kept in the contract and refused.</summary>
    [Theory]
    [InlineData("?scopeType=Project")]
    [InlineData("?scopeId=9c5e0000-0000-4000-8000-000000000999")]
    public async Task A_non_global_scope_is_refused(string query)
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Path("role.read") + query);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Contains(
                "global",
                body.RootElement.GetProperty("error").GetString()!,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>RW-A9: user.read alone is not enough; the other half is in WhoCanDoTests.</summary>
    [Fact]
    public async Task A_caller_holding_only_user_read_is_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.UserAdministrator, Path("role.read"));

            await AssertRefusedAsync(response);

            using var body = await JsonAsync(response);
            var error = body.RootElement.GetProperty("error").GetString()!;

            Assert.DoesNotContain("role.read", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user.read", error, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Without_a_carrier_the_answer_is_unauthorized()
    {
        await RunAsync(async (client, _) =>
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, Path("role.read"))).StatusCode);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, Path("no.such.permission"))).StatusCode);
        });
    }

    // ============================================================== harness

    private static string Path(string permissionCode)
        => $"/api/permissions/{Uri.EscapeDataString(permissionCode)}/holders";

    /// <summary>
    /// A live catalogue entry that no seeded role carries — today that is
    /// agent.manage, unheld precisely because the agents slice does not exist
    /// yet. Looked up rather than named, so it survives the code changing, but
    /// asserted explicitly: if a future seed grants every live permission, this
    /// test needs a new fixture and should say so rather than failing inside a
    /// LINQ operator.
    /// </summary>
    private static async Task<string> UnheldCodeAsync()
    {
        await using var context = CreateContext();

        var held = context.Set<RolePermission>().Where(x => x.RevokedAt == null).Select(x => x.PermissionId);

        var code = await context.Set<Permission>()
            .Where(x => x.IsActive && !held.Contains(x.Id))
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        Assert.False(
            code is null,
            "This test needs one live permission that no seeded role carries, to prove "
            + "'exists but nobody holds it' is distinguishable from 'retired' and from "
            + "'unknown'. Every live permission is now granted to some role; seed an "
            + "unheld one for this test.");

        return code!;
    }

    private static string[] Members(JsonElement element)
        => [.. element.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal)];

    private sealed record Callers(string Reviewer, string UserAdministrator);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, AccessReviewer, "aut-q7-access-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, UserAdministrator, "aut-q7-user-administrator", "user-administrator"));

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
                assignmentReason: "Reverse lookup tests", createdAt: now, createdBy: User.SystemUserId));

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
