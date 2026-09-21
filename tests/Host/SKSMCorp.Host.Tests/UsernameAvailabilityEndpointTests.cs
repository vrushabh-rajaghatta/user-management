using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static SKSMCorp.Host.Tests.RateLimitHarness;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// IDN-Q3 over HTTP (docs/requirements.md, "IDN-Q3 CheckUsernameAvailable on
/// Create user", UA-3 and UA-4), against the seeded role compositions.
///
/// The typed username travels in a POST body so that it never reaches a
/// request URL — and so never an access log. UA-4 checks that directly: no log
/// line the host writes for the request contains it.
///
/// Two permanent callers (a0a30000…): a user administrator and a security
/// administrator. Their own usernames serve as values that are certainly taken.
/// </summary>
public sealed class UsernameAvailabilityEndpointTests
{
    private const string Path = "/api/identities/username-availability";

    private const string CallerPassword = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("a0a30000-0000-4000-8000-000000000001"), Guid.Parse("a0a30000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("a0a30000-0000-4000-8000-000000000011"), Guid.Parse("a0a30000-0000-4000-8000-000000000012"));

    private const string AdministratorUsername = "permanent-idn-q3-user-administrator";

    /// <summary>UA-4: exactly { available }, for a taken and an unused username.</summary>
    [Fact]
    public async Task The_answer_is_exactly_available_true_or_false()
    {
        await RunAsync(async (client, administrator, _) =>
        {
            var taken = await CheckAsync(client, administrator, AdministratorUsername.ToUpperInvariant());
            var unused = await CheckAsync(client, administrator, $"unused-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
            Assert.Equal("""{"available":false}""", await taken.Content.ReadAsStringAsync());
            Assert.Equal("""{"available":true}""", await unused.Content.ReadAsStringAsync());
        });
    }

    /// <summary>UA-3 over HTTP.</summary>
    [Fact]
    public async Task Without_identity_read_or_a_carrier_or_a_username_it_is_refused()
    {
        await RunAsync(async (client, administrator, security) =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await CheckAsync(client, security, "anyone")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await CheckAsync(client, carrier: null, "anyone")).StatusCode);

            var blank = await CheckAsync(client, administrator, "  ");
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            Assert.Equal(
                "A username is required.",
                JsonDocument.Parse(await blank.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        });
    }

    /// <summary>
    /// "Local usernames refuse surrounding whitespace" (UW-9): the shared domain
    /// rule's sentence, the same one USR-C1 gives.
    /// </summary>
    [Fact]
    public async Task A_username_with_surrounding_whitespace_is_refused_with_the_rule()
    {
        await RunAsync(async (client, administrator, _) =>
        {
            foreach (var username in new[] { $" {AdministratorUsername}", $"{AdministratorUsername} ", "\u3000unused" })
            {
                var response = await CheckAsync(client, administrator, username);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(
                    "A username cannot begin or end with whitespace.",
                    JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
            }
        });
    }

    /// <summary>UA-4: POST with a body only — never a query string.</summary>
    [Fact]
    public async Task A_query_string_is_not_a_way_to_ask()
    {
        await RunAsync(async (client, administrator, _) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Path}?username={AdministratorUsername}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", administrator);

            var response = await client.SendAsync(request);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        });
    }

    /// <summary>UA-4: the typed value is in no log line the host writes for the request.</summary>
    [Fact]
    public async Task The_typed_username_is_never_logged()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var logs = new CapturedLogs();
        await using var factory = new HostFactory(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var client = factory.CreateClient();

        var administrator = await EnsureCallerAsync(
            client, UserAdministrator, "idn-q3-user-administrator", "user-administrator");

        var typed = $"secret-candidate-{Guid.NewGuid():N}";

        Assert.Equal(HttpStatusCode.OK, (await CheckAsync(client, administrator, typed)).StatusCode);

        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, x => x.Message.Contains(typed, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------- harness

    private static async Task RunAsync(Func<HttpClient, string, string, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var administrator = await EnsureCallerAsync(
            client, UserAdministrator, "idn-q3-user-administrator", "user-administrator");
        var security = await EnsureCallerAsync(
            client, SecurityAdministrator, "idn-q3-security-administrator", "security-administrator");

        await body(client, administrator, security);
    }

    private static async Task<HttpResponseMessage> CheckAsync(HttpClient client, string? carrier, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = JsonContent.Create(new { Username = username }),
        };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(identifiers.User)))
                await SeedActorAsync(client, identifiers, label, roleCode);
        }

        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = $"permanent-{label}", Password = CallerPassword });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
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
                assignmentReason: "IDN-Q3 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync(
            "/api/account/activate", new { Token = material.PlainText, NewPassword = CallerPassword }))
            .EnsureSuccessStatusCode();
    }

    private static SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);
}
