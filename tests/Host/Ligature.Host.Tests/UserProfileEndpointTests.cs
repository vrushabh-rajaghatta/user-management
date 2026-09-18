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
/// USR-C2 and USR-Q1 v1 over HTTP (docs/requirements.md, "USR-C2 — Update
/// User Profile, and USR-Q1 GetUser (narrow v1)", E-A1).
///
/// Two permanent callers: a user administrator (user.read, user.update) and
/// an access reviewer (user.read only). The users read and changed are
/// subjects, not actors, so each test seeds its own and deletes it.
/// </summary>
public sealed class UserProfileEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9e000000-0000-4000-8000-000000000001"), Guid.Parse("9e000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9e000000-0000-4000-8000-000000000011"), Guid.Parse("9e000000-0000-4000-8000-000000000012"));

    /// <summary>GetUser: exactly four members, to any user.read holder.</summary>
    [Fact]
    public async Task GetUser_answers_exactly_the_four_profile_fields()
    {
        await RunAsync(async (client, admin, reviewer, target) =>
        {
            foreach (var caller in new[] { admin, reviewer })
            {
                var response = await GetAsync(client, caller, $"/api/users/{target}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(
                    ["displayName", "firstName", "lastName", "userId"],
                    body.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
                Assert.Equal(target, body.RootElement.GetProperty("userId").GetGuid());
                Assert.Equal("Life", body.RootElement.GetProperty("firstName").GetString());
                Assert.Equal("Cycle", body.RootElement.GetProperty("lastName").GetString());
            }
        });
    }

    /// <summary>GetUser: the System actor is unknown, as is a missing user; no carrier is 401.</summary>
    [Fact]
    public async Task GetUser_refuses_the_system_actor_and_an_unknown_user_as_unknown()
    {
        await RunAsync(async (client, admin, _, _) =>
        {
            await AssertErrorAsync(await GetAsync(client, admin, $"/api/users/{User.SystemUserId.Value}"), "The user does not exist.");
            await AssertErrorAsync(await GetAsync(client, admin, $"/api/users/{Guid.NewGuid()}"), "The user does not exist.");

            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, null, $"/api/users/{Guid.NewGuid()}")).StatusCode);
        });
    }

    /// <summary>USR-C2: 204, no body, and the change is what GetUser then reads.</summary>
    [Fact]
    public async Task An_update_answers_204_and_is_read_back()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var response = await PostAsync(client, admin, $"/api/users/{target}/profile",
                new { FirstName = " Ada ", LastName = "Lovelace", DisplayName = "Ada L." });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());

            using var body = JsonDocument.Parse(await (await GetAsync(client, admin, $"/api/users/{target}")).Content.ReadAsStringAsync());

            Assert.Equal("Ada", body.RootElement.GetProperty("firstName").GetString());
            Assert.Equal("Ada L.", body.RootElement.GetProperty("displayName").GetString());
        });
    }

    /// <summary>USR-C2 refusals: a missing field, a rule, the System actor, the permission, no carrier.</summary>
    [Fact]
    public async Task Update_refusals_are_400_with_the_reason_and_no_carrier_is_401()
    {
        await RunAsync(async (client, admin, reviewer, target) =>
        {
            var missing = await PostAsync(client, admin, $"/api/users/{target}/profile", new { FirstName = "Ada", LastName = "Lovelace" });
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{target}/profile", new { FirstName = "Ada", LastName = "Lovelace", DisplayName = "   " }),
                "Display name is required.");

            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{User.SystemUserId.Value}/profile", new { FirstName = "S", LastName = "Y", DisplayName = "S" }),
                "This user's profile cannot be changed.");

            var refused = await PostAsync(client, reviewer, $"/api/users/{target}/profile", new { FirstName = "Ada", LastName = "Lovelace", DisplayName = "Ada" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("does not have permission", await ErrorAsync(refused));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostAsync(client, null, $"/api/users/{target}/profile", new { FirstName = "A", LastName = "B", DisplayName = "C" })).StatusCode);
        });
    }

    // ================================================================ harness

    private static async Task RunAsync(Func<HttpClient, string, string, Guid, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, UserAdministrator, "usr-c2-user-administrator", "user-administrator");
        var reviewer = await EnsureCallerAsync(client, AccessReviewer, "usr-c2-access-reviewer", "access-reviewer");
        var target = await SeedTargetAsync();

        try
        {
            await body(client, admin, reviewer, target);
        }
        finally
        {
            await DeleteTargetAsync(target);
        }
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await ErrorAsync(response));
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();

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

    private static async Task<string> StatusAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM app_user WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", userId);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        var username = $"permanent-{label}";

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(identifiers.User)))
                await SeedActorAsync(client, identifiers, label, username, roleCode);
        }

        return await SignInAsync(client, username);
    }

    private static async Task<string> SignInAsync(HttpClient client, string username)
    {
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task SeedActorAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string username, string? roleCode)
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

            if (roleCode is not null)
            {
                var roleId = await context.Set<Role>().Where(x => x.Code == roleCode).Select(x => x.Id).SingleAsync();

                context.Add(UserRole.Create(
                    UserRoleId.New(), user.Id, ActorType.Human, roleId, ScopeType.Global, scopeId: null,
                    effectiveFrom: now, effectiveTo: null, assignedAt: now, assignedBy: User.SystemUserId,
                    assignmentReason: "USR-C2 endpoint tests", createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SeedTargetAsync()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'Life', 'Cycle', 'Life Cycle {unique[..8]}', 'life-cycle-{unique}@example.test',
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'life-cycle-{unique[..16]}', 'Active', now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return userId;
    }

    private static LigatureDbContext CreateContext()
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    private static async Task DeleteTargetAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_token WHERE user_identity_id IN (SELECT id FROM user_identity WHERE user_id = @id)",
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
