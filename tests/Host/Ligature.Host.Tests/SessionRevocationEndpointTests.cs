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
/// SES-C3 and SES-C4 over HTTP. What the endpoints own: 204 with no body, the
/// binding 400s, 401 without a carrier, and — for the self form — that the
/// current session is the one presented. Selection rules are proven by the
/// integration tests.
///
/// Callers are pinned and never removed, for CreateUserEndpointTests' reason.
/// The account holder is pinned too, for AuthenticationEndToEndTests' reason —
/// signing out everywhere makes them the actor of records — and their sessions
/// and credential are reset per run.
/// </summary>
public sealed class SessionRevocationEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("4c7e2a90-1b3d-4f58-8e6a-9d2c5b7f1a30"),
         Guid.Parse("4c7e2a90-1b3d-4f58-8e6a-9d2c5b7f1a31"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("6e3b9d14-7a2c-4e81-b5f0-2c8d4a6e9b40"),
         Guid.Parse("6e3b9d14-7a2c-4e81-b5f0-2c8d4a6e9b41"));

    private static readonly (Guid User, Guid Identity) Holder =
        (Guid.Parse("2d9f6c83-5b1e-4a07-9c3d-8e4f1b7a2c50"),
         Guid.Parse("2d9f6c83-5b1e-4a07-9c3d-8e4f1b7a2c51"));

    private const string HolderUsername = "permanent-sessions-holder";

    // ---------------------------------------------------------------- SES-C3

    [Fact]
    public async Task Revoking_a_session_is_204_with_no_body_and_ends_it()
    {
        await RunAsync(async (client, admin, _) =>
        {
            var target = await SignInHolderAsync(client);
            var session = await LatestHolderSessionAsync();

            var response = await PostAsync(client, admin, $"/api/sessions/{session}/revoke", new { Reason = "SUP-1" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, target)).StatusCode);
        });
    }

    [Fact]
    public async Task Revoking_a_session_refuses_the_unprivileged_a_missing_or_blank_reason_and_no_carrier()
    {
        await RunAsync(async (client, admin, plain) =>
        {
            var target = await SignInHolderAsync(client);
            var session = await LatestHolderSessionAsync();
            var path = $"/api/sessions/{session}/revoke";

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, plain, path, new { Reason = "no" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, admin, path, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, admin, path, new { Reason = "  " })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, null, path, new { Reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PostAsync(client, admin, $"/api/sessions/{Guid.NewGuid()}/revoke", new { Reason = "x" })).StatusCode);

            // Still signed in: nothing above ended the session.
            Assert.Equal(HttpStatusCode.NoContent, (await SignOutAsync(client, target)).StatusCode);
        });
    }

    // --------------------------------------------------- SES-C4, administrator

    [Fact]
    public async Task Signing_a_user_out_everywhere_is_204_with_no_body_and_ends_every_session()
    {
        await RunAsync(async (client, admin, plain) =>
        {
            var first = await SignInHolderAsync(client);
            var second = await SignInHolderAsync(client);
            var path = $"/api/users/{Holder.User}/sign-out-everywhere";

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, plain, path, new { Reason = "no" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, admin, path, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, null, path, new { Reason = "x" })).StatusCode);

            var response = await PostAsync(client, admin, path, new { Reason = "SUP-2" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, first)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, second)).StatusCode);
        });
    }

    // ------------------------------------------------------------ SES-C4, self

    /// <summary>
    /// Two real carriers. Keeping the current session ends only the other; then
    /// an empty body ends the kept one too.
    /// </summary>
    [Fact]
    public async Task Signing_out_everywhere_keeps_only_the_presenting_session_when_asked()
    {
        await RunAsync(async (client, _, _) =>
        {
            var here = await SignInHolderAsync(client);
            var elsewhere = await SignInHolderAsync(client);
            const string Path = "/api/account/sign-out-everywhere";

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, null, Path, new { })).StatusCode);

            var keep = await PostAsync(client, here, Path, new { KeepCurrentSession = true });

            Assert.Equal(HttpStatusCode.NoContent, keep.StatusCode);
            Assert.Equal(string.Empty, await keep.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, elsewhere)).StatusCode);

            // No body at all: the default — this session is included.
            var all = await PostAsync(client, here, Path, payload: null);

            Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, here)).StatusCode);
        });
    }

    [Fact]
    public async Task The_endpoints_appear_in_the_api_document()
    {
        await using var factory = new HostFactory("true");

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/sessions/{sessionId}/revoke",
                     "/api/users/{userId}/sign-out-everywhere",
                     "/api/account/sign-out-everywhere",
                 })
        {
            Assert.True(paths.TryGetProperty(path, out var entry), $"The OpenAPI document does not describe {path}.");

            var description = entry.GetProperty("post").GetProperty("description").GetString() ?? "";

            Assert.Contains("204", description, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------ harness

    private static async Task RunAsync(Func<HttpClient, string, string, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, Administrator, "sessions-endpoint-administrator", privileged: true);
        var plain = await EnsureCallerAsync(client, Unprivileged, "sessions-endpoint-unprivileged", privileged: false);

        await ResetHolderAsync();
        await SeedHolderAsync(client);

        try
        {
            await body(client, admin, plain);
        }
        finally
        {
            await ResetHolderAsync();
        }
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) ids, string label, bool privileged)
    {
        var username = $"permanent-{label}";

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(ids.User)))
            {
                var activation = await SeedPendingAsync(ids, label, username, privileged ? "user-administrator" : null);

                (await client.PostAsJsonAsync("/api/account/activate", new { Token = activation, NewPassword = Password }))
                    .EnsureSuccessStatusCode();
            }
        }

        return (await SignInAsync(client, username))!;
    }

    private static async Task SeedHolderAsync(HttpClient client)
    {
        var activation = await SeedPendingAsync(Holder, "sessions-holder", HolderUsername, role: null);

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = activation, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A pending account: the pinned user and identity if they do not exist yet,
    /// and a fresh activation token either way.
    /// </summary>
    private static async Task<string> SeedPendingAsync(
        (Guid User, Guid Identity) ids, string label, string username, string? role)
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = CreateContext();

        if (!await context.Set<User>().AnyAsync(x => x.Id == new UserId(ids.User)))
        {
            var user = User.CreateHuman(
                new UserId(ids.User), "Permanent", "Caller", $"Permanent {label}",
                $"permanent-{label}@example.test", now, User.SystemUserId);

            var identity = UserIdentity.CreateLocal(
                new UserIdentityId(ids.Identity), user.Id, ActorType.Human, username, now, User.SystemUserId);

            context.AddRange(user, identity);

            if (role is not null)
            {
                var roleId = await context.Set<Role>().Where(x => x.Code == role).Select(x => x.Id).SingleAsync();

                context.Add(UserRole.Create(
                    UserRoleId.New(), user.Id, ActorType.Human, roleId, ScopeType.Global, scopeId: null,
                    effectiveFrom: now, effectiveTo: null, assignedAt: now, assignedBy: User.SystemUserId,
                    assignmentReason: "Session revocation endpoint tests",
                    createdAt: now, createdBy: User.SystemUserId));
            }
        }

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        context.Add(UserToken.Create(
            tokenId, new UserIdentityId(ids.Identity), TokenType.Activation, material.Hash,
            now, now.AddHours(72), User.SystemUserId));

        await context.SaveChangesAsync(CancellationToken.None);

        return material.PlainText;
    }

    /// <summary>Everything the holder accumulated, without touching the holder.</summary>
    private static async Task ResetHolderAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM password_history WHERE user_identity_id = @id",
            "DELETE FROM credential WHERE user_identity_id = @id",
            "DELETE FROM user_token WHERE user_identity_id = @id",
            "DELETE FROM user_session WHERE user_identity_id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", Holder.Identity);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static Task<string?> SignInHolderAsync(HttpClient client) => SignInAsync(client, HolderUsername)!;

    private static async Task<string?> SignInAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("accessToken").GetString();
    }

    private static async Task<Guid> LatestHolderSessionAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT id FROM user_session WHERE user_identity_id = @id ORDER BY created_at DESC LIMIT 1", connection);

        command.Parameters.AddWithValue("id", Holder.Identity);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static Task<HttpResponseMessage> SignOutAsync(HttpClient client, string? carrier)
        => PostAsync(client, carrier, "/api/auth/sign-out", payload: null);

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, string path, object? payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (payload is not null)
            request.Content = JsonContent.Create(payload);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }
}
