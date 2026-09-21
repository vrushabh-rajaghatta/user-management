using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// AUT-C7/C8 over HTTP (docs/requirements.md, "AUT-C7 AddPermissionToRole and
/// AUT-C8 RemovePermissionFromRole", RG-A17).
///
/// The answers follow AUT-C1 and AUT-C2, the grant/revoke pair for user_role:
/// 201 with the new grant's id, and 204. Nothing about the ROLE changes, so
/// there is no stored representation for the page to learn from.
///
/// Every role these tests create is deleted afterwards, grants and all.
/// </summary>
public sealed class RolePermissionEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c9e0000-0000-4000-8000-000000000001"), Guid.Parse("9c9e0000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c9e0000-0000-4000-8000-000000000011"), Guid.Parse("9c9e0000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task A_grant_is_201_with_its_id_and_a_revocation_is_204()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateRoleAsync(client, security, created);
            var permission = await PermissionAsync("user.read");

            var added = await PostAsync(client, security, $"/api/roles/{role}/permissions", new { PermissionId = permission });

            Assert.Equal(HttpStatusCode.Created, added.StatusCode);

            using var body = JsonDocument.Parse(await added.Content.ReadAsStringAsync());

            Assert.Equal(["rolePermissionId"], body.RootElement.EnumerateObject().Select(x => x.Name));

            var grantId = body.RootElement.GetProperty("rolePermissionId").GetGuid();

            var removed = await PostAsync(
                client, security, $"/api/role-permissions/{grantId}/revoke", new { Reason = "No longer needed." });

            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        });
    }

    [Fact]
    public async Task Refusals_are_400_with_the_reason_and_no_carrier_is_401()
    {
        await RunAsync(async (client, security, userAdmin, created) =>
        {
            var role = await CreateRoleAsync(client, security, created);
            var permission = await PermissionAsync("user.read");
            var path = $"/api/roles/{role}/permissions";

            // Missing fields refused where the request is bound.
            await AssertErrorAsync(
                await PostAsync(client, security, path, new { }),
                "permissionId is required.");

            await AssertErrorAsync(
                await PostAsync(client, security, $"/api/roles/{Guid.NewGuid()}/permissions", new { PermissionId = permission }),
                "The role does not exist.");

            await AssertErrorAsync(
                await PostAsync(client, security, path, new { PermissionId = Guid.NewGuid() }),
                "The permission does not exist.");

            var seeded = await SeededRoleAsync("access-reviewer");

            await AssertErrorAsync(
                await PostAsync(client, security, $"/api/roles/{seeded}/permissions", new { PermissionId = permission }),
                "System roles cannot be modified.");

            var added = await PostAsync(client, security, path, new { PermissionId = permission });
            var grantId = JsonDocument.Parse(await added.Content.ReadAsStringAsync())
                .RootElement.GetProperty("rolePermissionId").GetGuid();

            await AssertErrorAsync(
                await PostAsync(client, security, path, new { PermissionId = permission }),
                "The role already has this permission.");

            var revoke = $"/api/role-permissions/{grantId}/revoke";

            await AssertErrorAsync(
                await PostAsync(client, security, revoke, new { }),
                "A reason is required.");

            await AssertErrorAsync(
                await PostNullBodyAsync(client, security, revoke),
                "A reason is required.");

            await AssertErrorAsync(
                await PostAsync(client, security, revoke, new { Reason = "   " }),
                "A reason is required to revoke a permission from a role.");

            await AssertErrorAsync(
                await PostAsync(client, security, $"/api/role-permissions/{Guid.NewGuid()}/revoke", new { Reason = "Gone." }),
                "The role permission does not exist.");

            var refused = await PostAsync(client, userAdmin, path, new { PermissionId = permission });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("does not have permission", await ErrorAsync(refused));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostAsync(client, null, path, new { PermissionId = permission })).StatusCode);
        });
    }

    /// <summary>RG-A15: the reads follow, and their shapes do not move.</summary>
    [Fact]
    public async Task The_reads_show_the_grant_arriving_and_leaving()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateRoleAsync(client, security, created);
            var permission = await PermissionAsync("user.create");

            var added = await PostAsync(
                client, security, $"/api/roles/{role}/permissions", new { PermissionId = permission });

            var grantId = JsonDocument.Parse(await added.Content.ReadAsStringAsync())
                .RootElement.GetProperty("rolePermissionId").GetGuid();

            Assert.Contains("user.create", await GrantCodesAsync(client, security, role));
            Assert.False(await AgentAssignableAsync(client, security, role));

            await PostAsync(client, security, $"/api/role-permissions/{grantId}/revoke", new { Reason = "Reverting." });

            Assert.DoesNotContain("user.create", await GrantCodesAsync(client, security, role));
            Assert.True(await AgentAssignableAsync(client, security, role));
        });
    }

    private static async Task<IReadOnlyList<string?>> GrantCodesAsync(HttpClient client, string carrier, Guid role)
    {
        var response = await GetAsync(client, carrier, $"/api/roles/{role}/permissions");

        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. body.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())];
    }

    private static async Task<bool> AgentAssignableAsync(HttpClient client, string carrier, Guid role)
    {
        var response = await GetAsync(client, carrier, "/api/roles/administration");

        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("roles").EnumerateArray()
            .Single(x => x.GetProperty("roleId").GetGuid() == role)
            .GetProperty("agentAssignable").GetBoolean();
    }

    private static async Task<Guid> PermissionAsync(string code)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT id FROM permission WHERE code = '{code}'", connection);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> CreateRoleAsync(HttpClient client, string carrier, List<Guid> created)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/roles")
        {
            Content = JsonContent.Create(new
            {
                Code = $"tenant-{Guid.NewGuid():N}"[..24],
                Name = "Quality Reviewer",
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        var roleId = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("roleId").GetGuid();

        created.Add(roleId);

        return roleId;
    }

    private static async Task<Guid> SeededRoleAsync(string code)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT id FROM role WHERE code = '{code}'", connection);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostNullBodyAsync(HttpClient client, string carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
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

    private static async Task AssertErrorAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();

    private static async Task RunAsync(Func<HttpClient, string, string, List<Guid>, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var security = await EnsureCallerAsync(client, SecurityAdministrator, "aut-c7-security-administrator", "security-administrator");
        var userAdmin = await EnsureCallerAsync(client, UserAdministrator, "aut-c7-user-administrator", "user-administrator");

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
                assignmentReason: "AUT-C7 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

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
