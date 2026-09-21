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
/// CRD-C6 over HTTP. What the endpoint owns: 204 with no body, the binding
/// 400, and the refusals on the established surface. The eligibility, lock and
/// self-unlock rules are proven by the integration tests; one locked target is
/// enough here.
///
/// Callers are seeded once and never removed, with identifiers of their own,
/// for CreateUserEndpointTests' reason.
/// </summary>
public sealed class UnlockAccountEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("8a41c7d2-5e93-4b0f-9c26-3d7e1f4a8b50"),
         Guid.Parse("8a41c7d2-5e93-4b0f-9c26-3d7e1f4a8b51"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("b62e9f13-4d7a-4c85-a0e1-6f2b8c9d3e70"),
         Guid.Parse("b62e9f13-4d7a-4c85-a0e1-6f2b8c9d3e71"));

    [Fact]
    public async Task An_authorized_request_unlocks_with_no_body()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var response = await PostAsync(client, admin, target, new { Reason = "SUP-1: confirmed by phone." });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            Assert.False(await IsLockedAsync(target));
        });
    }

    [Fact]
    public async Task A_caller_without_the_permission_is_refused()
    {
        await RunAsync(async (client, _, plain, target) =>
        {
            var response = await PostAsync(client, plain, target, new { Reason = "Not mine to do." });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(await IsLockedAsync(target));
        });
    }

    [Fact]
    public async Task A_missing_or_blank_reason_is_refused()
    {
        await RunAsync(async (client, admin, _, target) =>
        {
            var missing = await PostAsync(client, admin, target, new { });
            var blank = await PostAsync(client, admin, target, new { Reason = "   " });

            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

            // The blank reason reached the command; its refusal is not the
            // binding check's.
            Assert.DoesNotContain(
                "A reason is required.\"", await blank.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            Assert.True(await IsLockedAsync(target));
        });
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await RunAsync(async (client, _, _, target) =>
        {
            var response = await PostAsync(client, carrier: null, target, new { Reason = "No carrier." });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(await IsLockedAsync(target));
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
                .TryGetProperty("/api/identities/{identityId}/unlock", out var path),
            "The OpenAPI document does not describe the unlock endpoint.");

        var description = path.GetProperty("post").GetProperty("description").GetString() ?? "";

        Assert.Contains("204", description, StringComparison.Ordinal);
        Assert.Contains("own account", description, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ harness

    private static async Task RunAsync(Func<HttpClient, string, string, Guid, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, privileged: true);
        var plain = await EnsureCallerAsync(client, privileged: false);

        var (user, identity) = await SeedLockedTargetAsync();

        try
        {
            await body(client, admin, plain, identity);
        }
        finally
        {
            await DeleteUserAsync(user);
        }
    }

    private static async Task<string> EnsureCallerAsync(HttpClient client, bool privileged)
    {
        var identifiers = privileged ? Administrator : Unprivileged;
        var label = privileged ? "unlock-endpoint-administrator" : "unlock-endpoint-unprivileged";
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

        return IssuedCarrier.From(signIn);
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
                        assignmentReason: "Unlock account endpoint tests",
                        createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>An eligible identity with a live lock.</summary>
    private static async Task<(Guid User, Guid Identity)> SeedLockedTargetAsync()
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
                 ('{userId}', 'Human', 'Unlock', 'Target', 'Unlock Target',
                  'unlock-target-{unique}@example.test', 'Active',
                  now(), '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'unlock-target-{unique}', 'Active', now(), '{system}');

             INSERT INTO credential
                 (id, user_identity_id, identity_type, password_hash,
                  password_algorithm, password_changed_at, must_change_password,
                  failed_attempt_count, locked_until, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', 'Local', '{hashed.Hash}',
                  '{hashed.Algorithm}', now(), false, 5, now() + interval '30 minutes',
                  now(), '{system}');
             """, connection);

        await command.ExecuteNonQueryAsync();

        return (userId, identityId);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, Guid identityId, object payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/identities/{identityId}/unlock")
        {
            Content = JsonContent.Create(payload),
        };

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<bool> IsLockedAsync(Guid identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT locked_until IS NOT NULL FROM credential WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", identityId);

        return (bool)(await command.ExecuteScalarAsync())!;
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

    /// <summary>
    /// The target is the SUBJECT of AccountUnlocked, never its actor, so no key
    /// prevents removing it.
    /// </summary>
    private static async Task DeleteUserAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            """
            DELETE FROM credential WHERE user_identity_id IN (
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
