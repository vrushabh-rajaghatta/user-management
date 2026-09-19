using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// SES-Q1 over HTTP (docs/requirements.md, "SES-Q1 GetActiveSessions and
/// Revoke on the User detail page", SQ-4, SQ-5, SQ-11 and the server half of
/// SQ-13), against the SEEDED role compositions.
///
/// `current` is server-derived from the carrier the request presents — the
/// caller's session id reaches the query the way it reaches /me — so these
/// tests hold it to the carrier, and prove nothing the client sends moves it.
///
/// Three permanent callers, one per seeded role (5e550000…). Their sessions
/// accumulate across runs until they expire, so no test here counts them:
/// each asserts about the sessions it made.
/// </summary>
public sealed class UserSessionsEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("5e550000-0000-4000-8000-000000000001"), Guid.Parse("5e550000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("5e550000-0000-4000-8000-000000000011"), Guid.Parse("5e550000-0000-4000-8000-000000000012"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("5e550000-0000-4000-8000-000000000021"), Guid.Parse("5e550000-0000-4000-8000-000000000022"));

    private static readonly string[] Fields =
        ["sessionId", "createdAt", "lastActivityAt", "expiresAt", "idleExpiresAt", "ipAddress", "userAgent", "current"];

    /// <summary>SQ-5 over HTTP: exactly the eight fields, and `current` follows the carrier presented.</summary>
    [Fact]
    public async Task Current_follows_the_carrier_presented_and_each_row_has_the_eight_fields()
    {
        await RunAsync(async (client, callers) =>
        {
            var second = await SignInAsync(client, "ses-q1-user-administrator");

            var asFirst = await ReadAsync(client, callers.UserAdministrator, UserAdministrator.User);
            var asSecond = await ReadAsync(client, second, UserAdministrator.User);

            foreach (var row in asFirst)
                Assert.Equal(Fields.Order(), row.EnumerateObject().Select(x => x.Name).Order());

            Assert.Equal([SessionOf(callers.UserAdministrator)], CurrentOf(asFirst));
            Assert.Equal([SessionOf(second)], CurrentOf(asSecond));

            // Both are listed either way: current marks one row, it filters nothing.
            Assert.Contains(asFirst, x => x.GetProperty("sessionId").GetGuid() == SessionOf(second));
        });
    }

    /// <summary>SQ-5: nothing the client sends sets `current`.</summary>
    [Fact]
    public async Task Nothing_in_the_request_moves_current()
    {
        await RunAsync(async (client, callers) =>
        {
            var second = await SignInAsync(client, "ses-q1-user-administrator");
            var path = $"/api/users/{UserAdministrator.User}/sessions?callerSessionId={SessionOf(second)}&current={SessionOf(second)}";

            var response = await GetAsync(client, callers.UserAdministrator, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rows = await RowsAsync(response);
            Assert.Equal([SessionOf(callers.UserAdministrator)], CurrentOf(rows));
        });
    }

    /// <summary>SQ-4 and SQ-11: the seeded roles, and the refusals.</summary>
    [Fact]
    public async Task The_seeded_roles_read_or_are_refused_as_decided()
    {
        await RunAsync(async (client, callers) =>
        {
            var path = $"/api/users/{UserAdministrator.User}/sessions";

            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.UserAdministrator, path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.AccessReviewer, path)).StatusCode);

            var refused = await GetAsync(client, callers.SecurityAdministrator, path);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, carrier: null, path)).StatusCode);
        });
    }

    [Fact]
    public async Task An_unknown_user_and_the_system_actor_are_refused_as_unknown()
    {
        await RunAsync(async (client, callers) =>
        {
            foreach (var target in new[] { Guid.NewGuid(), User.SystemUserId.Value })
            {
                var response = await GetAsync(client, callers.AccessReviewer, $"/api/users/{target}/sessions");

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(
                    "The user does not exist.",
                    JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
            }
        });
    }

    /// <summary>
    /// The server half of SQ-13 step 2: a listed session, revoked through the
    /// unchanged SES-C3, is gone from the next read, and its carrier is refused.
    /// </summary>
    [Fact]
    public async Task A_revoked_session_leaves_the_list_and_its_carrier_is_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            var reviewerSecond = await SignInAsync(client, "ses-q1-access-reviewer");
            var target = SessionOf(reviewerSecond);

            var before = await ReadAsync(client, callers.UserAdministrator, AccessReviewer.User);
            Assert.Contains(before, x => x.GetProperty("sessionId").GetGuid() == target);

            var revoke = await PostAsync(
                client, callers.UserAdministrator, $"/api/sessions/{target}/revoke", new { Reason = "SES-Q1 endpoint test" });
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

            var after = await ReadAsync(client, callers.UserAdministrator, AccessReviewer.User);
            Assert.DoesNotContain(after, x => x.GetProperty("sessionId").GetGuid() == target);

            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, reviewerSecond, "/api/me")).StatusCode);
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Callers(string UserAdministrator, string SecurityAdministrator, string AccessReviewer);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, UserAdministrator, "ses-q1-user-administrator", "user-administrator"),
            await EnsureCallerAsync(client, SecurityAdministrator, "ses-q1-security-administrator", "security-administrator"),
            await EnsureCallerAsync(client, AccessReviewer, "ses-q1-access-reviewer", "access-reviewer"));

        await body(client, callers);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadAsync(HttpClient client, string carrier, Guid user)
    {
        var response = await GetAsync(client, carrier, $"/api/users/{user}/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await RowsAsync(response);
    }

    private static async Task<IReadOnlyList<JsonElement>> RowsAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessions").EnumerateArray().Select(x => x.Clone()).ToList();

    private static Guid[] CurrentOf(IEnumerable<JsonElement> rows)
        => rows.Where(x => x.GetProperty("current").GetBoolean())
            .Select(x => x.GetProperty("sessionId").GetGuid())
            .ToArray();

    /// <summary>The session a carrier names, as the host's own AccessCarrier reads it.</summary>
    private static Guid SessionOf(string carrier)
        => new AccessCarrier(
                SigningKeyRing.Load(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                        [
                            new KeyValuePair<string, string?>(SigningKeyRing.CurrentKeySetting, HostFactory.PrimaryKeyId),
                            new KeyValuePair<string, string?>("LIGATURE_SIGNING_KEY_V1", HostFactory.PrimaryKey),
                        ])
                        .Build()))
            .Verify(carrier)?.Value
           ?? throw new InvalidOperationException("The carrier did not verify.");

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string? carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> SignInAsync(HttpClient client, string label)
    {
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = $"permanent-{label}", Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(identifiers.User)))
                await SeedActorAsync(client, identifiers, label, roleCode);
        }

        return await SignInAsync(client, label);
    }

    private static async Task SeedActorAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(identifiers.User), "Permanent", "Caller", $"Permanent {label}",
            $"permanent-{label}@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(identifiers.Identity), user.Id, ActorType.Human, $"permanent-{label}", now, User.SystemUserId);

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
                assignmentReason: "SES-Q1 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);
}
