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
/// PRV-C1 Amendment 1, S6 (docs/requirements.md): a caller holding ONLY
/// security-administrator, over HTTP.
///
/// Page-level and action-level access are independent. The amendment gives
/// this caller the Users list and nothing else in user management: every
/// user, identity and session command below stays refused, each by its own
/// permission, and before the target is looked at. The refusals held before
/// the amendment too; they are here so that a composition which over-grants
/// fails by name.
///
/// The shared database reaches the amended composition through catalogue
/// synchronisation, as any existing tenant does.
/// </summary>
public sealed class SecurityAdministratorEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("9c000000-0000-4000-8000-000000000001"), Guid.Parse("9c000000-0000-4000-8000-000000000002"));

    [Fact]
    public async Task A_security_administrator_may_list_users()
    {
        await RunAsync(async (client, carrier, _) =>
        {
            var response = await GetAsync(client, carrier, "/api/users");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    public static TheoryData<string, string> UserManagementCommands => new()
    {
        { "user.create", "/api/users" },
        { "user.resetpassword", "/api/users/{user}/password-reset" },
        { "user.create (CRD-C7)", "/api/users/{user}/activation-link" },
        { "session.revoke (all of a user's)", "/api/users/{user}/sign-out-everywhere" },
        { "user.unlock", "/api/identities/{identity}/unlock" },
        { "session.revoke (one)", "/api/sessions/{session}/revoke" },
    };

    [Theory]
    [MemberData(nameof(UserManagementCommands))]
    public async Task A_security_administrator_is_refused_every_user_management_command(string permission, string route)
    {
        await RunAsync(async (client, carrier, target) =>
        {
            var path = route
                .Replace("{user}", target.User.ToString())
                .Replace("{identity}", target.Identity.ToString())
                .Replace("{session}", Guid.NewGuid().ToString());

            var response = await PostAsync(client, carrier, path, new
            {
                FirstName = "Not",
                LastName = "Created",
                DisplayName = "Not Created",
                Email = $"not-created-{Guid.NewGuid():N}@example.test",
                InitialUsername = $"not-created-{Guid.NewGuid():N}"[..24],
                Reason = "A security administrator attempting user management.",
            });

            // A permission refusal is a 400 today, told apart from a validation
            // failure only by its message (Known Gaps, "Authorization failures
            // are not distinguishable from validation failures"). The message
            // is asserted, so a payload this test got wrong cannot pass as a
            // refusal.
            var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("error").GetString();

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest && error!.Contains("does not have permission"),
                $"{permission}: {route} answered {(int)response.StatusCode} \"{error}\", expected a permission refusal");
        });
    }

    // ================================================================ harness

    private static async Task RunAsync(Func<HttpClient, string, (Guid User, Guid Identity), Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var carrier = await EnsureCallerAsync(client);
        var target = await SeedTargetAsync();

        try
        {
            await body(client, carrier, target);
        }
        finally
        {
            await DeleteTargetAsync(target.User);
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> EnsureCallerAsync(HttpClient client)
    {
        const string username = "permanent-prv-c1-security-administrator";

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(SecurityAdministrator.User)))
                await SeedCallerAsync(client, username);
        }

        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task SeedCallerAsync(HttpClient client, string username)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(SecurityAdministrator.User), "Permanent", "Caller", "Permanent prv-c1-security-administrator",
            "permanent-prv-c1-security-administrator@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(SecurityAdministrator.Identity), user.Id, ActorType.Human, username, now, User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash, now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, identity, token);

            var roleId = await context.Set<Role>()
                .Where(x => x.Code == "security-administrator")
                .Select(x => x.Id)
                .SingleAsync();

            context.Add(UserRole.Create(
                UserRoleId.New(), user.Id, ActorType.Human, roleId, ScopeType.Global, scopeId: null,
                effectiveFrom: now, effectiveTo: null, assignedAt: now, assignedBy: User.SystemUserId,
                assignmentReason: "PRV-C1 Amendment 1 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static async Task<(Guid User, Guid Identity)> SeedTargetAsync()
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
             VALUES ('{userId}', 'Human', 'Sec', 'Admin', 'Sec Admin {unique[..8]}', 'sec-admin-{unique}@example.test',
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'sec-admin-{unique[..16]}', 'Active', now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return (userId, identityId);
    }

    private static SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    private static async Task DeleteTargetAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
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
