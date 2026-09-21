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
/// USR-Q3 over HTTP (docs/requirements.md, "USR-Q3 GetUserAccessSummary",
/// UA-A9 to UA-A12, and the route shape).
///
/// THE ROUTE IS NAMED FOR WHAT IT RETURNS: /effective-permissions, not the
/// catalogue's "access summary", which would overstate a response carrying no
/// roles and no dates (UA3).
///
/// THE NEGATIVE TEST IS THE POINT. A user administrator can read a user's core
/// profile and must NOT be able to read what that user can do — the boundary
/// DV-7 and DV-8 established, which this story must not open a second door
/// around. Both halves are asserted here against the same caller and the same
/// user, because that is the shape of the bypass.
/// </summary>
public sealed class UserEffectivePermissionsEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] PermissionMembers = ["code", "scopeId", "scopeType"];

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000311"), Guid.Parse("9c5e0000-0000-4000-8000-000000000312"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c5e0000-0000-4000-8000-000000000321"), Guid.Parse("9c5e0000-0000-4000-8000-000000000322"));

    [Fact]
    public async Task The_effective_set_is_answered_with_exactly_its_members()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Path(AccessReviewer.User));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(
                ["permissions", "status", "userId"],
                Members(body.RootElement));

            Assert.Equal(AccessReviewer.User.ToString(), body.RootElement.GetProperty("userId").GetString());
            Assert.Equal("Active", body.RootElement.GetProperty("status").GetString());

            var permissions = body.RootElement.GetProperty("permissions").EnumerateArray().ToList();

            Assert.NotEmpty(permissions);
            Assert.All(permissions, permission => Assert.Equal(PermissionMembers, Members(permission)));

            // Ordered by code, and deduplicated across roles.
            var codes = permissions.Select(x => x.GetProperty("code").GetString()!).ToList();

            Assert.Equal(codes.OrderBy(x => x, StringComparer.Ordinal), codes);
            Assert.Equal(codes.Distinct().Count(), codes.Count);

            // The access reviewer's own seeded composition.
            Assert.Contains("role.read", codes);
            Assert.Contains("user.read", codes);

            // Scope travels with each permission, and V1 is global.
            Assert.All(permissions, permission =>
            {
                Assert.Equal("Global", permission.GetProperty("scopeType").GetString());
                Assert.Equal(JsonValueKind.Null, permission.GetProperty("scopeId").ValueKind);
            });
        });
    }

    /// <summary>UA3: no roles, under any name.</summary>
    [Fact]
    public async Task The_response_carries_no_roles()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, Path(AccessReviewer.User));

            // ASSERT THE ANSWER FIRST. Against an error body this test would
            // pass for the wrong reason: a failure response contains no roles
            // either.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);
            var names = Members(body.RootElement);

            Assert.Contains("permissions", names);

            Assert.DoesNotContain("roles", names);
            Assert.DoesNotContain("assignments", names);
            Assert.DoesNotContain("effectiveFrom", names);
        });
    }

    /// <summary>
    /// UA-A10, THE BYPASS TEST. The same caller, the same user: the profile is
    /// readable and the effective set is not.
    /// </summary>
    [Fact]
    public async Task A_user_administrator_can_read_the_profile_but_not_the_effective_set()
    {
        await RunAsync(async (client, callers) =>
        {
            var profile = await GetAsync(
                client, callers.UserAdministrator, $"/api/users/{AccessReviewer.User}");

            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

            var refused = await GetAsync(
                client, callers.UserAdministrator, Path(AccessReviewer.User));

            await AssertRefusedAsync(refused);

            using var body = await JsonAsync(refused);
            var error = body.RootElement.GetProperty("error").GetString()!;

            Assert.DoesNotContain("user.read", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("role.read", error, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>UA-A11: an unknown user and the System actor are the same refusal.</summary>
    [Fact]
    public async Task An_unknown_user_and_the_system_actor_are_both_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            foreach (var subject in new[] { Guid.NewGuid(), User.SystemUserId.Value })
            {
                var response = await GetAsync(client, callers.Reviewer, Path(subject));

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

                using var body = await JsonAsync(response);

                Assert.Equal("The user does not exist.", body.RootElement.GetProperty("error").GetString());
            }
        });
    }

    [Fact]
    public async Task Without_a_carrier_the_answer_is_unauthorized()
    {
        await RunAsync(async (client, _) =>
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, Path(AccessReviewer.User))).StatusCode);
        });
    }

    /// <summary>A malformed user id is not this route at all.</summary>
    [Fact]
    public async Task A_malformed_user_id_does_not_match_the_route()
    {
        await RunAsync(async (client, callers) =>
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await GetAsync(client, callers.Reviewer, "/api/users/not-a-guid/effective-permissions")).StatusCode);
        });
    }

    // ============================================================== harness

    private static string Path(Guid userId) => $"/api/users/{userId}/effective-permissions";

    private static string[] Members(JsonElement element)
        => [.. element.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal)];

    private sealed record Callers(string Reviewer, string UserAdministrator);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, AccessReviewer, "usr-q3-access-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, UserAdministrator, "usr-q3-user-administrator", "user-administrator"));

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
                assignmentReason: "Access summary tests", createdAt: now, createdBy: User.SystemUserId));

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
