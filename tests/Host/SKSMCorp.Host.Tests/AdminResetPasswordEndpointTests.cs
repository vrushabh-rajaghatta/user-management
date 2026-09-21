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
/// CRD-C5 over HTTP.
///
/// What the endpoint itself owns: 202 with NO body — the administrator never
/// receives the token — and the refusals mapped onto the established surface.
/// The command's eligibility rules are proven by the integration tests; here a
/// single eligible target is enough.
///
/// Callers are seeded once and never removed, for CreateUserEndpointTests'
/// reason, with identifiers of their own so the two classes cannot race on
/// the same rows.
/// </summary>
public sealed class AdminResetPasswordEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("e0000000-0000-4000-8000-000000000001"),
         Guid.Parse("e0000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("f0000000-0000-4000-8000-000000000001"),
         Guid.Parse("f0000000-0000-4000-8000-000000000002"));

    [Fact]
    public async Task An_authorized_request_is_accepted_with_no_body()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var response = await PostAsync(client, admin.Carrier, target, new { Reason = "Suspected compromise." });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // No body at all — in particular no token, which would hand the
            // administrator the one thing they must never hold.
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            Assert.Equal(1, await ScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE user_identity_id = @id AND token_type = 'PasswordReset'
                  AND created_by = @admin
                """, target.Identity, admin.UserId.Value));

            Assert.Equal(1, await ScalarAsync(
                """
                SELECT count(*) FROM notification n
                JOIN user_token t ON t.id = n.token_id
                WHERE t.user_identity_id = @id AND n.notification_type = 'AdminPasswordReset'
                """, target.Identity));
        });
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await RunAsync(async (client, _, _, target) =>
        {
            var response = await PostAsync(client, carrier: null, target, new { Reason = "No carrier." });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, await TokensAsync(target));
        });
    }

    [Fact]
    public async Task A_caller_without_the_permission_is_refused()
    {
        await RunAsync(async (client, _, plain, target) =>
        {
            var response = await PostAsync(client, plain.Carrier, target, new { Reason = "Not mine to do." });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, await TokensAsync(target));
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
            var missing = await PostAsync(client, admin.Carrier, target, new { });
            var blank = await PostAsync(client, admin.Carrier, target, new { Reason = "   " });

            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

            // The blank reason reached the command: its refusal is the
            // command's, not the binding check's.
            Assert.DoesNotContain(
                "A reason is required.\"", await blank.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            Assert.Equal(0, await TokensAsync(target));
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
                .TryGetProperty("/api/users/{userId}/password-reset", out var path),
            "The OpenAPI document does not describe the admin password reset endpoint.");

        var description = path.GetProperty("post").GetProperty("description").GetString() ?? "";

        Assert.Contains("202", description, StringComparison.Ordinal);
        Assert.Contains("never", description, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ harness

    private sealed record Caller(UserId UserId, string Carrier);

    private sealed record Target(Guid User, Guid Identity);

    private static async Task RunAsync(Func<HttpClient, Caller, Caller, Target, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, privileged: true);
        var plain = await EnsureCallerAsync(client, privileged: false);

        var target = await SeedTargetAsync();

        try
        {
            await body(client, admin, plain, target);
        }
        finally
        {
            await DeleteUserAsync(target.User);
        }
    }

    private static async Task<Caller> EnsureCallerAsync(HttpClient client, bool privileged)
    {
        var identifiers = privileged ? Administrator : Unprivileged;
        var label = privileged ? "reset-endpoint-administrator" : "reset-endpoint-unprivileged";
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
                        assignmentReason: "Admin reset password endpoint tests",
                        createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var activation = await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password });

        activation.EnsureSuccessStatusCode();
    }

    /// <summary>An eligible target: active human, one local identity, a credential.</summary>
    private static async Task<Target> SeedTargetAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var hashed = new PasswordHasher().Hash("the-target-password-1");

        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Reset', 'Target', 'Reset Target',
                  'reset-target-{unique}@example.test', 'Active',
                  now(), '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'reset-target-{unique}', 'Active', now(), '{system}');

             INSERT INTO credential
                 (id, user_identity_id, identity_type, password_hash,
                  password_algorithm, password_changed_at, must_change_password,
                  failed_attempt_count, locked_until, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', 'Local', '{hashed.Hash}',
                  '{hashed.Algorithm}', now(), false, 0, NULL, now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return new Target(userId, identityId);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, Target target, object payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/users/{target.User}/password-reset")
        {
            Content = JsonContent.Create(payload),
        };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static SKSMCorpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options;

        return new SKSMCorpDbContext(options);
    }

    private static Task<long> TokensAsync(Target target)
        => ScalarAsync(
            "SELECT count(*) FROM user_token WHERE user_identity_id = @id", target.Identity);

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
    /// The target is the SUBJECT of the records CRD-C5 writes, never their
    /// actor, so no key prevents removing it.
    /// </summary>
    private static async Task DeleteUserAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            """
            DELETE FROM password_history WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            """
            DELETE FROM credential WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
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
