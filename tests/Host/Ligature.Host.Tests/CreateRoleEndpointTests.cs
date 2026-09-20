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
/// AUT-C3 over HTTP (docs/requirements.md, "AUT-C3 CreateRole", RC-A10).
///
/// Two permanent callers: a security administrator, who holds role.manage, and
/// a user administrator, who does not. Every role these tests create is
/// deleted afterwards — the shared database is not a place to leave roles,
/// which no command can remove.
/// </summary>
public sealed class CreateRoleEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private const string Path = "/api/roles";

    private static readonly string[] Members =
        ["code", "description", "isActive", "isSystemRole", "name", "roleId"];

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c6e0000-0000-4000-8000-000000000001"), Guid.Parse("9c6e0000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c6e0000-0000-4000-8000-000000000011"), Guid.Parse("9c6e0000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task A_created_role_is_201_with_exactly_its_members()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var code = $"tenant-{Guid.NewGuid():N}"[..24];

            var response = await PostAsync(client, security, new { Code = code, Name = "  Quality Reviewer ", Description = " Reviews access. " });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;

            created.Add(root.GetProperty("roleId").GetGuid());

            Assert.Equal(Members, root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.Equal("Quality Reviewer", root.GetProperty("name").GetString());
            Assert.Equal("Reviews access.", root.GetProperty("description").GetString());
            Assert.False(root.GetProperty("isSystemRole").GetBoolean());
            Assert.True(root.GetProperty("isActive").GetBoolean());
        });
    }

    [Fact]
    public async Task Refusals_are_400_with_the_reason_and_no_carrier_is_401()
    {
        await RunAsync(async (client, security, userAdmin, created) =>
        {
            var code = $"tenant-{Guid.NewGuid():N}"[..24];

            var first = await PostAsync(client, security, new { Code = code, Name = "Quality Reviewer" });
            created.Add(JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("roleId").GetGuid());

            // A missing field is refused where the request is bound.
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, security, new { Name = "No code" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, security, new { Code = code })).StatusCode);

            await AssertErrorAsync(
                await PostAsync(client, security, new { Code = " spaced", Name = "Quality Reviewer" }),
                "A role code cannot begin or end with whitespace.");

            await AssertErrorAsync(
                await PostAsync(client, security, new { Code = code, Name = "Another Name" }),
                "A role with this code already exists.");

            await AssertErrorAsync(
                await PostAsync(client, security, new { Code = code.ToUpperInvariant(), Name = "Another Name" }),
                "A role with this code already exists.");

            var refused = await PostAsync(client, userAdmin, new { Code = $"tenant-{Guid.NewGuid():N}"[..24], Name = "Quality Reviewer" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("does not have permission", await ErrorAsync(refused));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostAsync(client, null, new { Code = $"tenant-{Guid.NewGuid():N}"[..24], Name = "Quality Reviewer" })).StatusCode);
        });
    }

    // ============================================================== harness

    private static async Task RunAsync(Func<HttpClient, string, string, List<Guid>, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var security = await EnsureCallerAsync(client, SecurityAdministrator, "aut-c3-security-administrator", "security-administrator");
        var userAdmin = await EnsureCallerAsync(client, UserAdministrator, "aut-c3-user-administrator", "user-administrator");

        var created = new List<Guid>();

        try
        {
            await body(client, security, userAdmin, created);
        }
        finally
        {
            foreach (var roleId in created)
                await DeleteRoleAsync(roleId);
        }
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? carrier, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(payload) };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task DeleteRoleAsync(Guid roleId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_role WHERE role_id = @id",
            "DELETE FROM role_permission WHERE role_id = @id",
            "DELETE FROM role WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", roleId);
            await command.ExecuteNonQueryAsync();
        }
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
                assignmentReason: "AUT-C3 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

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
