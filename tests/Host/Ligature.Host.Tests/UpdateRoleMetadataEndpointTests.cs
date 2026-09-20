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
/// AUT-C4 over HTTP (docs/requirements.md, "AUT-C4 UpdateRoleMetadata",
/// RM-A12).
///
/// POST, not PUT: every mutation in this host is POST /api/{collection}/{id}/
/// {noun}, and RM6 keeps AUT-C4 in that convention rather than inventing REST
/// semantics from the verb.
///
/// THE ANSWER IS THE STORED ROLE (RM6), which is how a caller learns the
/// normalised name without normalising anything itself.
///
/// Every role these tests create is deleted afterwards, as AUT-C3's are — the
/// shared database is not a place to leave roles, and no command can remove one.
/// </summary>
public sealed class UpdateRoleMetadataEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] Members =
        ["code", "description", "isActive", "isSystemRole", "name", "roleId"];

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c7e0000-0000-4000-8000-000000000001"), Guid.Parse("9c7e0000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c7e0000-0000-4000-8000-000000000011"), Guid.Parse("9c7e0000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task An_edit_is_200_with_exactly_the_stored_members()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            var response = await PostAsync(
                client, security, role.RoleId, new { Name = "  Access Reviewer  ", Description = " Reviews everything. " });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;

            Assert.Equal(Members, root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(role.RoleId, root.GetProperty("roleId").GetGuid());
            Assert.Equal(role.Code, root.GetProperty("code").GetString());
            Assert.Equal("Access Reviewer", root.GetProperty("name").GetString());
            Assert.Equal("Reviews everything.", root.GetProperty("description").GetString());
            Assert.False(root.GetProperty("isSystemRole").GetBoolean());
            Assert.True(root.GetProperty("isActive").GetBoolean());
        });
    }

    /// <summary>RM3 over HTTP: a no-op is a success, and it answers the stored role like any other.</summary>
    [Fact]
    public async Task A_no_op_is_200_with_the_stored_role()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            var response = await PostAsync(
                client, security, role.RoleId, new { Name = "  Quality Reviewer  ", Description = " Reviews access. " });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal("Quality Reviewer", body.RootElement.GetProperty("name").GetString());
        });
    }

    /// <summary>RM2: a code in the payload is not a field. It is bound away, and the stored code stands.</summary>
    [Fact]
    public async Task A_code_in_the_payload_does_not_change_the_code()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            var response = await PostAsync(
                client, security, role.RoleId,
                new { Name = "Access Reviewer", Code = "something-else", IsSystemRole = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(role.Code, body.RootElement.GetProperty("code").GetString());
            Assert.False(body.RootElement.GetProperty("isSystemRole").GetBoolean());
        });
    }

    [Fact]
    public async Task Refusals_are_400_with_the_reason_and_no_carrier_is_401()
    {
        await RunAsync(async (client, security, userAdmin, created) =>
        {
            var role = await CreateAsync(client, security, created);

            // A missing field is refused where the request is bound, with the
            // binding's own sentence -- not the domain's, which would mean the
            // null had travelled all the way into the command.
            await AssertErrorAsync(
                await PostAsync(client, security, role.RoleId, new { Description = "No name" }),
                "name is required.");

            // An absent body binds the request itself to null. The guard is
            // what makes that a refusal rather than a dereference on the way
            // to the command.
            await AssertErrorAsync(
                await PostNullBodyAsync(client, security, role.RoleId),
                "name is required.");

            await AssertErrorAsync(
                await PostAsync(client, security, role.RoleId, new { Name = "   " }),
                "A role name is required.");

            await AssertErrorAsync(
                await PostAsync(client, security, role.RoleId, new { Name = new string('x', 101) }),
                "A role name must be at most 100 characters.");

            await AssertErrorAsync(
                await PostAsync(client, security, await SeededRoleAsync("access-reviewer"), new { Name = "Renamed" }),
                "System roles cannot be modified.");

            await AssertErrorAsync(
                await PostAsync(client, security, Guid.NewGuid(), new { Name = "Renamed" }),
                "The role does not exist.");

            var refused = await PostAsync(client, userAdmin, role.RoleId, new { Name = "Access Reviewer" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("does not have permission", await ErrorAsync(refused));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostAsync(client, null, role.RoleId, new { Name = "Access Reviewer" })).StatusCode);
        });
    }

    /// <summary>RM-A12: the reads this story does not own keep answering exactly as they did.</summary>
    [Fact]
    public async Task The_reads_are_undisturbed_and_show_the_new_name()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            await PostAsync(client, security, role.RoleId, new { Name = "Access Reviewer" });

            var administration = await GetAsync(client, security, "/api/roles/administration");
            Assert.Equal(HttpStatusCode.OK, administration.StatusCode);

            using var listed = JsonDocument.Parse(await administration.Content.ReadAsStringAsync());

            var row = listed.RootElement.GetProperty("roles").EnumerateArray()
                .Single(x => x.GetProperty("roleId").GetGuid() == role.RoleId);

            Assert.Equal("Access Reviewer", row.GetProperty("name").GetString());
            Assert.Equal(role.Code, row.GetProperty("code").GetString());

            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, security, "/api/roles")).StatusCode);
            Assert.Equal(
                HttpStatusCode.OK,
                (await GetAsync(client, security, $"/api/roles/{role.RoleId}/permissions")).StatusCode);
        });
    }

    // ============================================================== harness

    private static string PathFor(Guid roleId) => $"/api/roles/{roleId}/metadata";

    private static async Task<(Guid RoleId, string? Code)> CreateAsync(
        HttpClient client, string carrier, List<Guid> created)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/roles")
        {
            Content = JsonContent.Create(new
            {
                Code = $"tenant-{Guid.NewGuid():N}"[..24],
                Name = "Quality Reviewer",
                Description = "Reviews access.",
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var roleId = root.GetProperty("roleId").GetGuid();

        created.Add(roleId);

        return (roleId, root.GetProperty("code").GetString());
    }

    private static async Task<Guid> SeededRoleAsync(string code)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT id FROM role WHERE code = '{code}'", connection);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task RunAsync(Func<HttpClient, string, string, List<Guid>, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var security = await EnsureCallerAsync(client, SecurityAdministrator, "aut-c4-security-administrator", "security-administrator");
        var userAdmin = await EnsureCallerAsync(client, UserAdministrator, "aut-c4-user-administrator", "user-administrator");

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

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, Guid roleId, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PathFor(roleId)) { Content = JsonContent.Create(payload) };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    /// <summary>A literal JSON null, which binds the request record to null.</summary>
    private static async Task<HttpResponseMessage> PostNullBodyAsync(HttpClient client, string carrier, Guid roleId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PathFor(roleId))
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

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
                assignmentReason: "AUT-C4 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

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
