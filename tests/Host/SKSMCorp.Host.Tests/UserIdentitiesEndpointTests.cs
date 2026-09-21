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
/// IDN-Q1 over HTTP and CRD-C6's server authority (docs/requirements.md,
/// "IDN-Q1 GetUserIdentities and Unlock on the User detail page", ID-1, ID-7,
/// ID-8), against the SEEDED role compositions.
///
/// Three permanent callers, one per seeded role. The users read and unlocked
/// are subjects, seeded per test and deleted after.
/// </summary>
public sealed class UserIdentitiesEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly (Guid User, Guid Identity) UserAdministrator =
        (Guid.Parse("1d10a000-0000-4000-8000-000000000001"), Guid.Parse("1d10a000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) SecurityAdministrator =
        (Guid.Parse("1d10a000-0000-4000-8000-000000000011"), Guid.Parse("1d10a000-0000-4000-8000-000000000012"));

    private static readonly (Guid User, Guid Identity) AccessReviewer =
        (Guid.Parse("1d10a000-0000-4000-8000-000000000021"), Guid.Parse("1d10a000-0000-4000-8000-000000000022"));

    // ---------------------------------------------------------------- ID-1

    /// <summary>Exactly the eight amended fields; never the subject or the failure count.</summary>
    [Fact]
    public async Task The_read_answers_exactly_the_eight_fields()
    {
        await RunAsync(async (client, callers) =>
        {
            var target = await SeedTargetAsync(locked: true);

            try
            {
                var response = await GetAsync(client, callers.UserAdministrator, $"/api/users/{target.UserId}/identities");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var identity = Assert.Single(body.RootElement.GetProperty("identities").EnumerateArray());

                Assert.Equal(
                    ["deactivatedAt", "locked", "lockedUntil", "provider", "status", "type", "userIdentityId", "username"],
                    identity.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
                Assert.Equal(target.IdentityId, identity.GetProperty("userIdentityId").GetGuid());
                Assert.Equal("Local", identity.GetProperty("type").GetString());
                Assert.Equal("Application", identity.GetProperty("provider").GetString());
                Assert.Equal("Active", identity.GetProperty("status").GetString());
                Assert.True(identity.GetProperty("locked").GetBoolean());
                Assert.Equal(JsonValueKind.String, identity.GetProperty("lockedUntil").ValueKind);
            }
            finally
            {
                await DeleteTargetAsync(target.UserId);
            }
        });
    }

    [Fact]
    public async Task Unknown_users_the_System_actor_and_no_carrier_are_refused()
    {
        await RunAsync(async (client, callers) =>
        {
            await AssertErrorAsync(
                await GetAsync(client, callers.UserAdministrator, $"/api/users/{Guid.NewGuid()}/identities"),
                "The user does not exist.");
            await AssertErrorAsync(
                await GetAsync(client, callers.UserAdministrator, $"/api/users/{User.SystemUserId.Value}/identities"),
                "The user does not exist.");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await GetAsync(client, null, $"/api/users/{Guid.NewGuid()}/identities")).StatusCode);
        });
    }

    // ---------------------------------------------------------------- ID-8

    /// <summary>
    /// The seeded split: the user administrator and the access reviewer hold
    /// identity.read; the security administrator does not, and is refused.
    /// Only the user administrator holds user.unlock.
    /// </summary>
    [Fact]
    public async Task Identities_follow_identity_read_and_unlock_follows_user_unlock()
    {
        await RunAsync(async (client, callers) =>
        {
            var target = await SeedTargetAsync(locked: true);

            try
            {
                var path = $"/api/users/{target.UserId}/identities";

                Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.UserAdministrator, path)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, callers.AccessReviewer, path)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(client, callers.SecurityAdministrator, path)).StatusCode);

                var unlock = $"/api/identities/{target.IdentityId}/unlock";
                var reason = new { Reason = "ID-8 endpoint test" };

                Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, callers.AccessReviewer, unlock, reason)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, callers.SecurityAdministrator, unlock, reason)).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, callers.UserAdministrator, unlock, reason)).StatusCode);

                using var body = JsonDocument.Parse(
                    await (await GetAsync(client, callers.UserAdministrator, path)).Content.ReadAsStringAsync());
                var identity = Assert.Single(body.RootElement.GetProperty("identities").EnumerateArray());

                Assert.False(identity.GetProperty("locked").GetBoolean());
                Assert.Equal(JsonValueKind.Null, identity.GetProperty("lockedUntil").ValueKind);
            }
            finally
            {
                await DeleteTargetAsync(target.UserId);
            }
        });
    }

    // ---------------------------------------------------------------- ID-7

    /// <summary>
    /// The server's authority, independent of any client: an administrator is
    /// refused in BOTH forms of self-targeting — the caller's own session
    /// identity, and another identity of the caller's own user, even one that
    /// is locked. CRD-C6's rule is user-level. No AccountUnlocked either way.
    /// </summary>
    [Fact]
    public async Task An_administrator_cannot_unlock_any_identity_of_their_own_user()
    {
        await RunAsync(async (client, callers) =>
        {
            var second = await AddLockedIdentityAsync(UserAdministrator.User);

            try
            {
                var before = await AccountUnlockedCountAsync(UserAdministrator.User);
                var reason = new { Reason = "ID-7 endpoint test" };

                await AssertErrorAsync(
                    await PostAsync(client, callers.UserAdministrator, $"/api/identities/{UserAdministrator.Identity}/unlock", reason),
                    "An administrator cannot unlock their own account.");

                await AssertErrorAsync(
                    await PostAsync(client, callers.UserAdministrator, $"/api/identities/{second}/unlock", reason),
                    "An administrator cannot unlock their own account.");

                Assert.Equal(before, await AccountUnlockedCountAsync(UserAdministrator.User));
            }
            finally
            {
                await DeleteIdentityAsync(second);
            }
        });
    }

    // ================================================================ harness

    private sealed record Callers(string UserAdministrator, string SecurityAdministrator, string AccessReviewer);

    private sealed record Target(Guid UserId, Guid IdentityId);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, UserAdministrator, "idn-q1-user-administrator", "user-administrator"),
            await EnsureCallerAsync(client, SecurityAdministrator, "idn-q1-security-administrator", "security-administrator"),
            await EnsureCallerAsync(client, AccessReviewer, "idn-q1-access-reviewer", "access-reviewer"));

        await body(client, callers);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            message,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

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

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string roleCode)
    {
        var username = $"permanent-{label}";

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == new UserId(identifiers.User)))
                await SeedActorAsync(client, identifiers, label, username, roleCode);
        }

        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task SeedActorAsync(
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
                assignmentReason: "IDN-Q1 endpoint tests", createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>A human user with one local identity and a credential, locked in force or not.</summary>
    private static async Task<Target> SeedTargetAsync(bool locked)
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var unique = userId.ToString("N");
        var system = User.SystemUserId.Value;
        var hashed = new PasswordHasher().Hash("a-target-password-for-tests");

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{userId}', 'Human', 'Lock', 'Target', 'Lock Target {unique[..8]}', 'lock-target-{unique}@example.test',
                     'Active', now(), '{system}', now(), '{system}');

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'lock-target-{unique[..16]}', 'Active', now(), '{system}');

             INSERT INTO credential (id, user_identity_id, identity_type, password_hash, password_algorithm,
                                     password_changed_at, must_change_password, failed_attempt_count,
                                     locked_until, created_at, created_by)
             VALUES ('{Guid.NewGuid()}', '{identityId}', 'Local', '{hashed.Hash}', '{hashed.Algorithm}',
                     now(), false, {(locked ? 5 : 0)}, {(locked ? "now() + interval '1 hour'" : "NULL")}, now(), '{system}');
             """);

        return new Target(userId, identityId);
    }

    /// <summary>A second local identity for an existing user, with a credential locked in force.</summary>
    private static async Task<Guid> AddLockedIdentityAsync(Guid userId)
    {
        var identityId = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var hashed = new PasswordHasher().Hash("a-second-identity-password");

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                     '{identityId}', 'second-{identityId:N}', 'Active', now(), '{system}');

             INSERT INTO credential (id, user_identity_id, identity_type, password_hash, password_algorithm,
                                     password_changed_at, must_change_password, failed_attempt_count,
                                     locked_until, created_at, created_by)
             VALUES ('{Guid.NewGuid()}', '{identityId}', 'Local', '{hashed.Hash}', '{hashed.Algorithm}',
                     now(), false, 5, now() + interval '1 hour', now(), '{system}');
             """);

        return identityId;
    }

    private static async Task<long> AccountUnlockedCountAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'AccountUnlocked' AND e.entity_type = 'User' AND e.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", userId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeleteIdentityAsync(Guid identityId)
        => await ExecuteAsync(
            $"""
             DELETE FROM credential WHERE user_identity_id = '{identityId}';
             DELETE FROM user_identity WHERE id = '{identityId}';
             """);

    /// <summary>
    /// A target that was unlocked is referenced by its AccountUnlocked record,
    /// which nobody may delete; such a target is left in place, as audited
    /// subjects are elsewhere.
    /// </summary>
    private static async Task DeleteTargetAsync(Guid userId)
    {
        try
        {
            await ExecuteAsync(
                $"""
                 DELETE FROM credential WHERE user_identity_id IN (SELECT id FROM user_identity WHERE user_id = '{userId}');
                 DELETE FROM user_identity WHERE user_id = '{userId}';
                 DELETE FROM app_user WHERE id = '{userId}';
                 """);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
        }
    }

    private static SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
