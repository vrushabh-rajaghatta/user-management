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
/// USR-C4 / USR-C5 over HTTP (docs/requirements.md, "USR-C4 / USR-C5", E1, V9).
///
/// Two permanent callers: a user administrator (user.deactivate and
/// user.reactivate) and an access reviewer (neither). The users acted on are
/// subjects, not actors, so each test seeds its own and deletes it — except
/// the one that must sign in first, which is pinned and reset every run.
/// </summary>
public sealed class UserLifecycleEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("9d000000-0000-4000-8000-000000000001"), Guid.Parse("9d000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("9d000000-0000-4000-8000-000000000011"), Guid.Parse("9d000000-0000-4000-8000-000000000012"));

    /// <summary>Signs in, so it becomes an audit actor and can never be deleted: pinned and reset.</summary>
    private static readonly (Guid User, Guid Identity) Leaver =
        (Guid.Parse("9d000000-0000-4000-8000-000000000021"), Guid.Parse("9d000000-0000-4000-8000-000000000022"));

    /// <summary>E1 — the round trip, each step 204 with no body.</summary>
    [Fact]
    public async Task Deactivate_then_reactivate_answer_204_with_no_body()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var deactivated = await PostAsync(client, admin, $"/api/users/{target}/deactivate", new { Reason = "Left the company." });

            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
            Assert.Empty(await deactivated.Content.ReadAsStringAsync());
            Assert.Equal("Inactive", await StatusAsync(target));

            var reactivated = await PostAsync(client, admin, $"/api/users/{target}/reactivate", new { Reason = "Rehired." });

            Assert.Equal(HttpStatusCode.NoContent, reactivated.StatusCode);
            Assert.Empty(await reactivated.Content.ReadAsStringAsync());
            Assert.Equal("Active", await StatusAsync(target));
        });
    }

    /// <summary>E1 — every refusal is 400 with its message; nothing changes.</summary>
    [Fact]
    public async Task Refusals_are_400_with_the_reason_in_the_body()
    {
        await RunAsync(async (client, admin, reviewer, target) =>
        {
            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{target}/deactivate", new { Reason = " " }),
                "A reason is required to deactivate a user.");

            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{UserAdministrator.User}/deactivate", new { Reason = "Self." }),
                "A user cannot deactivate themselves.");

            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{target}/reactivate", new { Reason = "Not needed." }),
                "The user is already active.");

            await AssertErrorAsync(
                await PostAsync(client, admin, $"/api/users/{Guid.NewGuid()}/deactivate", new { Reason = "Unknown." }),
                "The user does not exist.");

            Assert.Equal("Active", await StatusAsync(target));
        });
    }

    /// <summary>E1 — the permission refusal carries the permission message (Known Gap: it is 400).</summary>
    [Fact]
    public async Task A_caller_without_the_permission_is_refused_and_no_carrier_is_401()
    {
        await RunAsync(async (client, _, reviewer, target) =>
        {
            foreach (var action in new[] { "deactivate", "reactivate" })
            {
                var refused = await PostAsync(client, reviewer, $"/api/users/{target}/{action}", new { Reason = "Not mine to do." });

                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("does not have permission", await ErrorAsync(refused));

                var anonymous = await PostAsync(client, null, $"/api/users/{target}/{action}", new { Reason = "Anonymous." });

                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }

            Assert.Equal("Active", await StatusAsync(target));
        });
    }

    /// <summary>
    /// V9 — a signed-in user is out on their next request, and cannot sign in
    /// again. The cascade revoked the session; the per-request check would
    /// have refused it anyway.
    /// </summary>
    [Fact]
    public async Task A_deactivated_user_is_signed_out_and_cannot_sign_in()
    {
        await RunAsync(async (client, admin, _, _) =>
        {
            var leaver = await EnsurePinnedLeaverAsync(client);

            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, leaver, "/api/me")).StatusCode);

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await PostAsync(client, admin, $"/api/users/{Leaver.User}/deactivate", new { Reason = "Left the company." })).StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, leaver, "/api/me")).StatusCode);

            var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = LeaverUsername, Password });

            Assert.False(signIn.IsSuccessStatusCode, "a deactivated user signed in");
        });
    }

    // ================================================================ harness

    private const string LeaverUsername = "permanent-usr-c4-leaver";

    private static async Task RunAsync(Func<HttpClient, string, string, Guid, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, UserAdministrator, "usr-c4-user-administrator", "user-administrator");
        var reviewer = await EnsureCallerAsync(client, AccessReviewer, "usr-c4-access-reviewer", "access-reviewer");
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

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

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

    /// <summary>
    /// The leaver, active and signed in. Each run starts by undoing the last
    /// run's deactivation in SQL — a test reset, not a reactivation: USR-C5 is
    /// not what is under test here, and must not be needed for the setup.
    /// </summary>
    private static async Task<string> EnsurePinnedLeaverAsync(HttpClient client)
    {
        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(Leaver.User)))
                await SeedActorAsync(client, Leaver, "usr-c4-leaver", LeaverUsername, roleCode: null);
        }

        await using (var connection = await TestDatabase.OpenAsync())
        {
            foreach (var sql in new[]
            {
                "UPDATE app_user SET status = 'Active', deactivated_at = NULL, deactivated_by = NULL WHERE id = @user",
                "UPDATE user_identity SET status = 'Active', deactivated_at = NULL, deactivated_by = NULL WHERE user_id = @user",
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("user", Leaver.User);
                await command.ExecuteNonQueryAsync();
            }
        }

        return await SignInAsync(client, LeaverUsername);
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
                    assignmentReason: "USR-C4/C5 endpoint tests", createdAt: now, createdBy: User.SystemUserId));
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
