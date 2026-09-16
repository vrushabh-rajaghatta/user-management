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
/// B6 — GET /api/me over HTTP, the first query to reach the endpoint surface.
///
/// The caller here deliberately holds TWO active identities. A user may, and
/// /me must report the one THIS SESSION was established with rather than
/// whichever the user happens to own — a distinction no single-identity fixture
/// could detect (D3).
///
/// As with the create-user suite, the caller is permanent: /me is not audited,
/// but signing in is, so a caller seeded fresh per run would leave undeletable
/// rows behind each time.
/// </summary>
public sealed class MeEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private const string UserAdministrator = "user-administrator";

    /// <summary>
    /// Distinct from every other suite's fixed identifiers. a0000000, b0000000,
    /// c0000000, e0000000 and f0000000 are already taken by other permanent
    /// callers, and reusing one is not a clash the seeding notices: it finds the
    /// user present, skips seeding, and then fails to sign in as a username that
    /// was never created.
    /// </summary>
    private static readonly (Guid User, Guid SignedIn, Guid Other) Caller =
        (Guid.Parse("d0000000-0000-4000-8000-000000000001"),
         Guid.Parse("d0000000-0000-4000-8000-000000000002"),
         Guid.Parse("d0000000-0000-4000-8000-000000000003"));

    private const string SignedInUsername = "permanent-me-signed-in";

    private const string OtherUsername = "permanent-me-other";

    /// <summary>
    /// D3. The user owns both identities and both are active; only one
    /// established this session, and that is the one /me must name.
    /// </summary>
    [Fact]
    public async Task The_identity_reported_is_the_one_the_session_was_established_with()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var carrier = await EnsureCallerAsync(client);

        var me = await ReadAsync(client, carrier);

        var identity = me.RootElement.GetProperty("identity");

        Assert.Equal(
            Caller.SignedIn,
            identity.GetProperty("userIdentityId").GetGuid());

        Assert.NotEqual(
            Caller.Other,
            identity.GetProperty("userIdentityId").GetGuid());

        Assert.Equal(
            SignedInUsername,
            identity.GetProperty("username").GetString());
    }

    /// <summary>
    /// The identity comes from a read taken when /me is asked, not from
    /// anything the session cached when it began.
    ///
    /// This does NOT claim to distinguish a live query from
    /// IExecutionContext.Identity — it cannot, because CallerEstablisher builds
    /// that snapshot from rows it reads on every request, so both would be
    /// current. What it proves is that /me reflects the record as it stands on
    /// each call.
    /// </summary>
    [Fact]
    public async Task The_display_name_reflects_the_record_as_it_stands_on_each_call()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var carrier = await EnsureCallerAsync(client);

        var before = await DisplayNameAsync(client, carrier);

        var renamed = $"Renamed {Guid.NewGuid():N}";

        await ExecuteAsync(
            "UPDATE app_user SET display_name = @value WHERE id = @id",
            renamed,
            Caller.User);

        var after = await DisplayNameAsync(client, carrier);

        Assert.NotEqual(before, after);
        Assert.Equal(renamed, after);
    }

    /// <summary>
    /// There is no anonymous form. A caller who cannot be established is
    /// refused, never described as unauthenticated in a 200 — the client's
    /// whole contract rests on 401 meaning "no authenticated caller" while a
    /// 5xx means "we do not know".
    /// </summary>
    [Fact]
    public async Task An_anonymous_request_is_refused_rather_than_described()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("authenticated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Effective permissions with their scopes, which is what the client needs
    /// and what roles would not give it.
    /// </summary>
    [Fact]
    public async Task The_effective_permissions_are_reported_with_their_scope()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var carrier = await EnsureCallerAsync(client);

        var me = await ReadAsync(client, carrier);

        var held = me.RootElement.GetProperty("permissions")
            .EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == "user.create");

        Assert.Equal("Global", held.GetProperty("scopeType").GetString());
        Assert.Equal(JsonValueKind.Null, held.GetProperty("scopeId").ValueKind);
    }

    [Fact]
    public async Task The_session_timing_is_reported()
    {
        await using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var carrier = await EnsureCallerAsync(client);

        var session = (await ReadAsync(client, carrier)).RootElement.GetProperty("session");

        Assert.True(session.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.True(session.GetProperty("idleExpiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    // ------------------------------------------------------------- harness

    private static async Task<string> DisplayNameAsync(HttpClient client, string carrier)
        => (await ReadAsync(client, carrier)).RootElement
            .GetProperty("identity")
            .GetProperty("displayName")
            .GetString()!;

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string carrier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> EnsureCallerAsync(HttpClient client)
    {
        await using (var existing = CreateContext())
        {
            if (await existing.Set<User>().AnyAsync(x => x.Id == new UserId(Caller.User)))
                return await SignInAsync(client, SignedInUsername);
        }

        await SeedAsync(client);

        return await SignInAsync(client, SignedInUsername);
    }

    private static async Task SeedAsync(HttpClient client)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(Caller.User), "Permanent", "Caller",
            "Permanent me caller",
            "permanent-me@example.test", now, User.SystemUserId);

        // Two active identities for one user. The unique index is on
        // (identity_provider, subject_id), so distinct usernames are all this
        // needs — and it is what makes D3 observable.
        var signedIn = UserIdentity.CreateLocal(
            new UserIdentityId(Caller.SignedIn), user.Id, ActorType.Human,
            SignedInUsername, now, User.SystemUserId);

        var other = UserIdentity.CreateLocal(
            new UserIdentityId(Caller.Other), user.Id, ActorType.Human,
            OtherUsername, now, User.SystemUserId);

        var tokenService = new UserTokenService();
        var tokenId = UserTokenId.New();
        var material = tokenService.Generate(tokenId);

        var token = UserToken.Create(
            tokenId, signedIn.Id, TokenType.Activation, material.Hash,
            now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, signedIn, other, token);

            var roleId = await context.Set<Role>()
                .Where(x => x.Code == UserAdministrator)
                .Select(x => x.Id)
                .SingleAsync();

            context.Add(
                UserRole.Create(
                    UserRoleId.New(), user.Id, ActorType.Human, roleId,
                    ScopeType.Global, scopeId: null,
                    effectiveFrom: now, effectiveTo: null,
                    assignedAt: now, assignedBy: User.SystemUserId,
                    assignmentReason: "Me endpoint tests",
                    createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var activation = await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password });

        activation.EnsureSuccessStatusCode();
    }

    private static async Task<string> SignInAsync(HttpClient client, string username)
    {
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task ExecuteAsync(string sql, string value, Guid id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("id", id);

        await command.ExecuteNonQueryAsync();
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }
}
