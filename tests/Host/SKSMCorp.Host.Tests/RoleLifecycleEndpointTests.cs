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
/// AUT-C5/C6 over HTTP (docs/requirements.md, "AUT-C5 DeactivateRole and
/// AUT-C6 ReactivateRole", RD-A12).
///
/// Two routes, one shape: POST /{id}/{verb}, answering the role AS STORED so
/// the caller knows which action to offer next without a second read.
///
/// Every role these tests create is deleted afterwards. AUT-C5 retires a role;
/// it does not remove one, and the shared database is not a place to leave them.
/// </summary>
public sealed class RoleLifecycleEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly string[] Members =
        ["code", "description", "isActive", "isSystemRole", "name", "roleId"];

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c8e0000-0000-4000-8000-000000000001"), Guid.Parse("9c8e0000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9c8e0000-0000-4000-8000-000000000011"), Guid.Parse("9c8e0000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task Deactivating_then_reactivating_is_200_with_the_stored_role()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            var off = await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "No longer used." });

            Assert.Equal(HttpStatusCode.OK, off.StatusCode);

            using (var body = JsonDocument.Parse(await off.Content.ReadAsStringAsync()))
            {
                var root = body.RootElement;

                Assert.Equal(Members, root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
                Assert.Equal(role.Code, root.GetProperty("code").GetString());
                Assert.False(root.GetProperty("isActive").GetBoolean());
                Assert.False(root.GetProperty("isSystemRole").GetBoolean());
            }

            var on = await PostAsync(client, security, role.RoleId, "reactivate", new { });

            Assert.Equal(HttpStatusCode.OK, on.StatusCode);

            using var back = JsonDocument.Parse(await on.Content.ReadAsStringAsync());

            Assert.True(back.RootElement.GetProperty("isActive").GetBoolean());
        });
    }

    /// <summary>RD4 over HTTP: the second call is a success, and says so with the stored role.</summary>
    [Fact]
    public async Task Repeating_either_call_is_200()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            Assert.Equal(HttpStatusCode.OK,
                (await PostAsync(client, security, role.RoleId, "reactivate", new { })).StatusCode);

            await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "Retired." });

            var again = await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "Retired again." });

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);

            using var body = JsonDocument.Parse(await again.Content.ReadAsStringAsync());

            Assert.False(body.RootElement.GetProperty("isActive").GetBoolean());
        });
    }

    /// <summary>RD5: reactivation takes no reason, and one in the payload binds to nothing.</summary>
    [Fact]
    public async Task Reactivation_ignores_a_reason_in_the_payload()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "Retired." });

            var on = await PostAsync(client, security, role.RoleId, "reactivate", new { Reason = "Ignored." });

            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
            Assert.Equal(0, await ReasonedReactivationsAsync(role.RoleId));
        });
    }

    [Fact]
    public async Task Refusals_are_400_with_the_reason_and_no_carrier_is_401()
    {
        await RunAsync(async (client, security, userAdmin, created) =>
        {
            var role = await CreateAsync(client, security, created);

            // The reason is refused where the request is bound, with the
            // binding's own sentence, and again when it is blank.
            await AssertErrorAsync(
                await PostAsync(client, security, role.RoleId, "deactivate", new { }),
                "A reason is required.");

            await AssertErrorAsync(
                await PostNullBodyAsync(client, security, role.RoleId),
                "A reason is required.");

            await AssertErrorAsync(
                await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "   " }),
                "A reason is required to deactivate a role.");

            var seeded = await SeededRoleAsync("access-reviewer");

            await AssertErrorAsync(
                await PostAsync(client, security, seeded, "deactivate", new { Reason = "Trying." }),
                "System roles cannot be deactivated.");

            await AssertErrorAsync(
                await PostAsync(client, security, seeded, "reactivate", new { }),
                "System roles cannot be reactivated.");

            await AssertErrorAsync(
                await PostAsync(client, security, Guid.NewGuid(), "deactivate", new { Reason = "Gone." }),
                "The role does not exist.");

            await AssertErrorAsync(
                await PostAsync(client, security, Guid.NewGuid(), "reactivate", new { }),
                "The role does not exist.");

            foreach (var verb in new[] { "deactivate", "reactivate" })
            {
                var refused = await PostAsync(client, userAdmin, role.RoleId, verb, new { Reason = "Trying." });

                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("does not have permission", await ErrorAsync(refused));

                Assert.Equal(
                    HttpStatusCode.Unauthorized,
                    (await PostAsync(client, null, role.RoleId, verb, new { Reason = "Trying." })).StatusCode);
            }
        });
    }

    /// <summary>RD-A9 over HTTP: the reads this story does not own change exactly as far as eligibility.</summary>
    [Fact]
    public async Task The_reads_show_the_role_leaving_and_returning()
    {
        await RunAsync(async (client, security, _, created) =>
        {
            var role = await CreateAsync(client, security, created);

            await PostAsync(client, security, role.RoleId, "deactivate", new { Reason = "Retired." });

            Assert.DoesNotContain(role.RoleId, await AdministrationAsync(client, security, includeInactive: false));
            Assert.Contains(role.RoleId, await AdministrationAsync(client, security, includeInactive: true));
            Assert.DoesNotContain(role.RoleId, await GrantableAsync(client, security));

            await PostAsync(client, security, role.RoleId, "reactivate", new { });

            Assert.Contains(role.RoleId, await AdministrationAsync(client, security, includeInactive: false));
            Assert.Contains(role.RoleId, await GrantableAsync(client, security));
        });
    }

    private static async Task<IReadOnlyList<Guid>> AdministrationAsync(
        HttpClient client, string carrier, bool includeInactive)
    {
        var response = await GetAsync(
            client, carrier, $"/api/roles/administration{(includeInactive ? "?includeInactive=true" : "")}");

        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. body.RootElement.GetProperty("roles").EnumerateArray()
            .Select(x => x.GetProperty("roleId").GetGuid())];
    }

    private static async Task<IReadOnlyList<Guid>> GrantableAsync(HttpClient client, string carrier)
    {
        var response = await GetAsync(client, carrier, "/api/roles");

        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var roles = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("roles");

        return [.. roles.EnumerateArray().Select(x => x.GetProperty("roleId").GetGuid())];
    }

    private static async Task<long> ReasonedReactivationsAsync(Guid roleId)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT count(*) FROM audit.audit_record
             WHERE entity_id = '{roleId}' AND event_type = 'RoleReactivated' AND reason IS NOT NULL
            """, connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    // ============================================================== harness


    private static string PathFor(Guid roleId, string verb) => $"/api/roles/{roleId}/{verb}";

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

        var security = await EnsureCallerAsync(client, SecurityAdministrator, "aut-c5-security-administrator", "security-administrator");
        var userAdmin = await EnsureCallerAsync(client, UserAdministrator, "aut-c5-user-administrator", "user-administrator");

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
        HttpClient client, string? carrier, Guid roleId, string verb, object? payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PathFor(roleId, verb))
        {
            Content = payload is null ? null : JsonContent.Create(payload),
        };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    /// <summary>A literal JSON null, which binds the request record to null.</summary>
    private static async Task<HttpResponseMessage> PostNullBodyAsync(HttpClient client, string carrier, Guid roleId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PathFor(roleId, "deactivate"))
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
                assignmentReason: "AUT-C5 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

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
