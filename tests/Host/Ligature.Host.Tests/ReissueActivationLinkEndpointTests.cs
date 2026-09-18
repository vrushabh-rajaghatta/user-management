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
/// CRD-C7 over HTTP.
///
/// What the endpoint itself owns: 202 with NO body (the administrator never
/// receives the token or the link), and the refusals mapped onto the
/// established surface. The command's eligibility rules and audit records are
/// proven by the integration tests. Here one pending target is enough, seeded
/// with a live activation link so every refusal can show it left that link
/// alone.
///
/// Callers are seeded once and never removed, for CreateUserEndpointTests'
/// reason, with identifiers of their own so no two classes race on the same
/// rows.
/// </summary>
public sealed class ReissueActivationLinkEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("c7000000-0000-4000-8000-000000000001"),
         Guid.Parse("c7000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("c7000000-0000-4000-8000-000000000011"),
         Guid.Parse("c7000000-0000-4000-8000-000000000012"));

    [Fact]
    public async Task An_authorized_request_is_accepted_with_no_body()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var response = await PostAsync(client, admin.Carrier, target.User, new { Reason = "Mail never arrived." });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // No body at all, and in particular no token or link.
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            // Exactly one open activation link, and it is not the prior one.
            Assert.Equal(1, await OpenLinksAsync(target));
            Assert.Equal(0, await ScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE id = @id AND used_at IS NULL AND invalidated_at IS NULL
                """, target.PriorToken));

            Assert.Equal(1, await ScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE user_identity_id = @id AND token_type = 'Activation'
                  AND used_at IS NULL AND invalidated_at IS NULL AND created_by = @admin
                """, target.Identity, admin.UserId.Value));

            Assert.Equal(1, await ScalarAsync(
                """
                SELECT count(*) FROM notification n
                JOIN user_token t ON t.id = n.token_id
                WHERE t.user_identity_id = @id AND n.notification_type = 'AccountActivation'
                  AND t.created_by = @admin
                """, target.Identity, admin.UserId.Value));
        });
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await RunAsync(async (client, _, _, target) =>
        {
            var response = await PostAsync(client, carrier: null, target.User, new { Reason = "No carrier." });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertUntouchedAsync(target);
        });
    }

    /// <summary>
    /// The unprivileged caller holds no role. 400, not 403: distinguishing the
    /// two remains a Known Gap, and this does not close it.
    /// </summary>
    [Fact]
    public async Task A_caller_without_user_create_is_refused()
    {
        await RunAsync(async (client, _, plain, target) =>
        {
            var response = await PostAsync(client, plain.Carrier, target.User, new { Reason = "Not mine to do." });

            await AssertErrorAsync(response);
            await AssertUntouchedAsync(target);
        });
    }

    /// <summary>
    /// A missing reason is refused by the endpoint's binding check; a blank one
    /// is dispatched and refused by the command. Both are the same 400 and
    /// neither writes anything.
    /// </summary>
    [Fact]
    public async Task A_missing_or_blank_reason_is_refused()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var missing = await PostAsync(client, admin.Carrier, target.User, new { });
            var blank = await PostAsync(client, admin.Carrier, target.User, new { Reason = "   " });

            await AssertErrorAsync(missing);
            await AssertErrorAsync(blank);

            await AssertUntouchedAsync(target);
        });
    }

    [Fact]
    public async Task An_unknown_user_is_refused()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var response = await PostAsync(client, admin.Carrier, Guid.NewGuid(), new { Reason = "Nobody." });

            await AssertErrorAsync(response);
            await AssertUntouchedAsync(target);
        });
    }

    /// <summary>
    /// The server's eligibility, over HTTP: a caller that believes a user is
    /// pending cannot make it so. The administrator has activated and holds a
    /// credential.
    /// </summary>
    [Fact]
    public async Task A_user_who_has_activated_is_refused()
    {
        await RunAsync(async (client, admin, _, _) =>
        {
            var response = await PostAsync(client, admin.Carrier, admin.UserId.Value, new { Reason = "Already active." });

            await AssertErrorAsync(response);

            Assert.Equal(0, await ScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE user_identity_id = @id AND used_at IS NULL AND invalidated_at IS NULL
                """, Administrator.Identity));
        });
    }

    /// <summary>
    /// The userId a USR-Q2 row carries is accepted, unchanged, and addresses
    /// the user the row describes.
    /// </summary>
    [Fact]
    public async Task A_user_list_rows_user_id_is_accepted()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var row = Assert.Single(
                await ReadAllRowsAsync(client, admin.Carrier),
                x => x.GetProperty("displayName").GetString() == target.DisplayName);

            var userId = Guid.Parse(row.GetProperty("userId").GetString()!);

            var response = await PostAsync(client, admin.Carrier, userId, new { Reason = "From the list." });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(target.User, userId);
            Assert.Equal(1, await OpenLinksAsync(target));
        });
    }

    [Fact]
    public async Task The_endpoint_appears_in_the_api_document()
    {
        await using var factory = new HostFactory("true");

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(
            document.RootElement.GetProperty("paths")
                .TryGetProperty("/api/users/{userId}/activation-link", out var path),
            "The OpenAPI document does not describe the activation-link endpoint.");

        var description = path.GetProperty("post").GetProperty("description").GetString() ?? "";

        Assert.Contains("user.create", description, StringComparison.Ordinal);
        Assert.Contains("202", description, StringComparison.Ordinal);
        Assert.Contains("never", description, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ harness

    private sealed record Caller(UserId UserId, string Carrier);

    private sealed record Target(Guid User, Guid Identity, Guid PriorToken, string DisplayName);

    private static async Task RunAsync(Func<HttpClient, Caller, Caller, Target, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, privileged: true);
        var plain = await EnsureCallerAsync(client, privileged: false);

        var target = await SeedPendingTargetAsync();

        try
        {
            await body(client, admin, plain, target);
        }
        finally
        {
            await DeleteUserAsync(target.User);
        }
    }

    /// <summary>
    /// The prior link is still the ONLY open one, no token was added, and no
    /// notification was queued.
    /// </summary>
    private static async Task AssertUntouchedAsync(Target target)
    {
        Assert.Equal(1, await ScalarAsync(
            "SELECT count(*) FROM user_token WHERE user_identity_id = @id", target.Identity));

        Assert.Equal(1, await ScalarAsync(
            """
            SELECT count(*) FROM user_token
            WHERE id = @id AND used_at IS NULL AND invalidated_at IS NULL
            """, target.PriorToken));

        Assert.Equal(0, await ScalarAsync(
            """
            SELECT count(*) FROM notification n
            JOIN user_token t ON t.id = n.token_id
            WHERE t.user_identity_id = @id
            """, target.Identity));
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(
            string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()),
            "A refusal must carry the host's error body.");
    }

    private static Task<long> OpenLinksAsync(Target target)
        => ScalarAsync(
            """
            SELECT count(*) FROM user_token
            WHERE user_identity_id = @id AND token_type = 'Activation'
              AND used_at IS NULL AND invalidated_at IS NULL
            """, target.Identity);

    private static async Task<Caller> EnsureCallerAsync(HttpClient client, bool privileged)
    {
        var identifiers = privileged ? Administrator : Unprivileged;
        var label = privileged ? "reissue-endpoint-administrator" : "reissue-endpoint-unprivileged";
        var username = $"permanent-{label}";
        var userId = new UserId(identifiers.User);

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == userId))
                await SeedCallerAsync(client, identifiers, label, username, privileged);
        }

        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return new Caller(userId, IssuedCarrier.From(signIn));
    }

    private static async Task SeedCallerAsync(
        HttpClient client,
        (Guid User, Guid Identity) identifiers,
        string label,
        string username,
        bool privileged)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(identifiers.User), "Permanent", "Caller", $"Permanent {label}",
            $"permanent-{label}@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(identifiers.Identity), user.Id, ActorType.Human,
            username, now, User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash,
            now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, identity, token);

            if (privileged)
            {
                var roleId = await context.Set<Role>()
                    .Where(x => x.Code == "user-administrator")
                    .Select(x => x.Id)
                    .SingleAsync();

                context.Add(
                    UserRole.Create(
                        UserRoleId.New(), user.Id, ActorType.Human, roleId,
                        ScopeType.Global, scopeId: null,
                        effectiveFrom: now, effectiveTo: null,
                        assignedAt: now, assignedBy: User.SystemUserId,
                        assignmentReason: "Reissue activation link endpoint tests",
                        createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var activation = await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password });

        activation.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A pending target: active human, one local identity, an email, NO
    /// credential, and one live activation link that nobody will use.
    /// </summary>
    private static async Task<Target> SeedPendingTargetAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);
        var system = User.SystemUserId.Value;
        var displayName = $"Pending Target {unique}";

        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Pending', 'Target', '{displayName}',
                  'pending-target-{unique}@example.test', 'Active',
                  now(), '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'pending-target-{unique}', 'Active', now(), '{system}');

             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{tokenId.Value}', '{identityId}', 'Activation', '{material.Hash}',
                  now() + interval '1 hour', NULL, NULL, now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return new Target(userId, identityId, tokenId.Value, displayName);
    }

    private static async Task<List<JsonElement>> ReadAllRowsAsync(HttpClient client, string carrier)
    {
        var rows = new List<JsonElement>();

        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/users?page={page}&pageSize=100");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

            var response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            rows.AddRange(body.RootElement.GetProperty("users").EnumerateArray().Select(x => x.Clone()));

            if (!body.RootElement.GetProperty("hasMore").GetBoolean())
                return rows;
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, Guid userId, object payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/users/{userId}/activation-link")
        {
            Content = JsonContent.Create(payload),
        };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static async Task<long> ScalarAsync(string sql, Guid id, Guid? admin = null)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("id", id);

        if (admin is not null)
            command.Parameters.AddWithValue("admin", admin.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// The target is the SUBJECT of the records CRD-C7 writes, never their
    /// actor, and it never activates, so no key prevents removing it.
    /// </summary>
    private static async Task DeleteUserAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            """
            UPDATE notification
            SET status = 'NotSent', not_sent_reason = 'Abandoned', closed_at = now()
            WHERE status = 'Pending' AND token_id IN (
                SELECT t.id FROM user_token t
                JOIN user_identity i ON i.id = t.user_identity_id
                WHERE i.user_id = @id)
            """,
            """
            DELETE FROM notification WHERE token_id IN (
                SELECT t.id FROM user_token t
                JOIN user_identity i ON i.id = t.user_identity_id
                WHERE i.user_id = @id)
            """,
            """
            DELETE FROM user_token WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
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
