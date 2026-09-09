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
/// USR-C1 over HTTP — the first AUTHORIZED endpoint.
///
/// Slice 1 proved a caller can be established and that an unestablished one is
/// refused. This proves the next thing: an established caller who lacks
/// "user.create" is refused as well, by the pipeline rather than by the
/// endpoint.
///
/// The two callers differ in exactly one respect — a user_role row — so any
/// difference in outcome is attributable to the permission and nothing else.
/// </summary>
public sealed class CreateUserEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private const string UserAdministrator = "user-administrator";

    /// <summary>
    /// The two callers are seeded once and never removed.
    ///
    /// USR-C1 is audited, and an audit record names its actor through a
    /// foreign key to app_user (AR10) and the assignment that authorised it
    /// through another to user_role (AR12). Audit rows cannot be deleted by
    /// anyone — the guard trigger is ENABLE ALWAYS — so from the first
    /// successful request onward, the caller and its role assignment are
    /// permanent. Seeding a fresh caller per test would leave two undeletable
    /// users behind on every run.
    ///
    /// So the identifiers are fixed. The first run creates the caller,
    /// activates it and signs it in; every later run signs the same caller in
    /// again. The users these tests CREATE are still deleted: a created user
    /// is the subject of a record, not its actor, and no key points at it.
    /// </summary>
    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("a0000000-0000-4000-8000-000000000001"),
         Guid.Parse("a0000000-0000-4000-8000-000000000002"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("b0000000-0000-4000-8000-000000000001"),
         Guid.Parse("b0000000-0000-4000-8000-000000000002"));

    // ------------------------------------------------------- authorization

    /// <summary>
    /// No carrier at all. Refused by AuthenticationBehavior before
    /// authorization is ever considered.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await RunAsync(async (client, _, _, cleanup) =>
        {
            var response = await PostAsync(client, carrier: null, Payload(cleanup));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            Assert.Equal(0, await CountUsersAsync(cleanup.Email));
        });
    }

    /// <summary>
    /// THE test this story exists for. A perfectly valid carrier, a perfectly
    /// valid request, and no "user.create" grant.
    ///
    /// 400 rather than 403 is a known limitation, not an oversight: the
    /// pipeline raises BusinessRuleViolationException for an authorization
    /// failure, which is the same type a duplicate email raises. Separating
    /// them needs a decision about error classification that is larger than
    /// this endpoint.
    /// </summary>
    [Fact]
    public async Task An_authenticated_caller_without_the_permission_is_refused()
    {
        await RunAsync(async (client, _, plain, cleanup) =>
        {
            var response = await PostAsync(client, plain.Carrier, Payload(cleanup));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // NOTHING was created. The refusal happened before the handler, so
            // there is no half-made user to find.
            Assert.Equal(0, await CountUsersAsync(cleanup.Email));
        });
    }

    /// <summary>
    /// The refusal must not name the permission code, the role that would grant
    /// it, or the command type. A caller learns that they may not do this, and
    /// nothing about the shape of the authorization model.
    /// </summary>
    [Fact]
    public async Task The_refusal_does_not_identify_the_missing_permission()
    {
        await RunAsync(async (client, _, plain, cleanup) =>
        {
            var body = await (await PostAsync(client, plain.Carrier, Payload(cleanup)))
                .Content.ReadAsStringAsync();

            foreach (var leak in new[]
                     {
                         "user.create", "user-administrator", "CreateUser",
                         "CreateUserCommand", "role", "Behavior",
                     })
            {
                Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    /// <summary>
    /// Recorded deliberately, because it is the part that surprises.
    ///
    /// Keeping 400 for authorization failures does NOT make them
    /// indistinguishable from validation failures — the message says
    /// "does not have permission", so a client can tell them apart by reading
    /// it. What 400 removes is the ability to branch on the distinction by
    /// STATUS, which pushes any client that needs to into string-matching the
    /// message: precisely what the host is forbidden from doing.
    ///
    /// This test exists so that the state of affairs is asserted rather than
    /// assumed, and so that whoever revisits error classification
    /// (docs/requirements.md) finds it written down.
    /// </summary>
    [Fact]
    public async Task The_two_kinds_of_400_differ_only_in_their_message()
    {
        await RunAsync(async (client, admin, plain, cleanup) =>
        {
            var unauthorized = await PostAsync(client, plain.Carrier, Payload(cleanup));

            var invalid = await PostAsync(
                client, admin.Carrier, new { FirstName = "OnlyThis" });

            Assert.Equal(unauthorized.StatusCode, invalid.StatusCode);

            var unauthorizedBody = await unauthorized.Content.ReadAsStringAsync();
            var invalidBody = await invalid.Content.ReadAsStringAsync();

            // Same status, different text. A client cannot distinguish these
            // programmatically without matching on the message.
            Assert.NotEqual(unauthorizedBody, invalidBody);

            Assert.Contains("permission", unauthorizedBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("permission", invalidBody, StringComparison.OrdinalIgnoreCase);

            // The malformed request is refused by the ENDPOINT, before any
            // command is dispatched — not by the domain further in. Asserting
            // the endpoint's own wording is what makes that check load-bearing;
            // without this, removing it changes nothing observable, because the
            // domain would reject the same request a layer later.
            Assert.Contains(
                "are required", invalidBody, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The same request, from a caller holding the permission.
    /// </summary>
    [Fact]
    public async Task An_authorized_caller_creates_a_user()
    {
        await RunAsync(async (client, admin, _, cleanup) =>
        {
            var response = await PostAsync(client, admin.Carrier, Payload(cleanup));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // 201 without Location: there is no read endpoint to point at, and
            // its absence does not make the 201 wrong.
            Assert.Null(response.Headers.Location);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            var userId = document.RootElement.GetProperty("userId").GetGuid();

            cleanup.Created.Add(userId);

            Assert.NotEqual(Guid.Empty, userId);

            Assert.NotEqual(
                Guid.Empty,
                document.RootElement.GetProperty("userIdentityId").GetGuid());

            // The body carries the two ids and NOTHING else — in particular no
            // activation token, which would hand a live credential to whoever
            // created the account.
            Assert.Equal(
                2, document.RootElement.EnumerateObject().Count());

            Assert.Equal(1, await CountUsersAsync(cleanup.Email));

            // Created with an identity and an activation token, and NO
            // credential — inv. 15, the pending-activation state.
            Assert.Equal(1, await ScalarAsync(
                "SELECT count(*) FROM user_identity WHERE user_id = @id", userId));

            Assert.Equal(1, await ScalarAsync(
                """
                SELECT count(*) FROM user_token WHERE user_identity_id IN (
                    SELECT id FROM user_identity WHERE user_id = @id)
                """, userId));

            Assert.Equal(0, await ScalarAsync(
                """
                SELECT count(*) FROM credential WHERE user_identity_id IN (
                    SELECT id FROM user_identity WHERE user_id = @id)
                """, userId));
        });
    }

    // ------------------------------------------------------ the two 400s

    /// <summary>
    /// The owner's question: do the two different 400s behave differently
    /// underneath?
    ///
    /// They must not. An authorization failure is refused INSIDE the pipeline
    /// and a malformed request is refused BEFORE it — different places, and
    /// neither may leave anything behind. If the endpoint ever dispatched a
    /// request it had already judged invalid, or ran the handler for a caller
    /// the pipeline had refused, this is where it would show.
    /// </summary>
    [Fact]
    public async Task Neither_kind_of_400_creates_anything()
    {
        await RunAsync(async (client, admin, plain, cleanup) =>
        {
            // Refused inside the pipeline, by AuthorizationBehavior.
            var unauthorized = await PostAsync(client, plain.Carrier, Payload(cleanup));

            // Refused before dispatch, by the endpoint's own binding check —
            // and by an AUTHORIZED caller, so authorization is not what stops
            // it.
            var malformed = await PostAsync(
                client, admin.Carrier, new { FirstName = "OnlyThis" });

            Assert.Equal(HttpStatusCode.BadRequest, unauthorized.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

            // Scoped to this fixture's own identifiers rather than a count of
            // every user in the database: the suites share one database and
            // run concurrently, so a global count answers a question about
            // other tests as much as about this one.
            Assert.Equal(0, await CountUsersAsync(cleanup.Email));
            Assert.Equal(0, await CountIdentitiesAsync(cleanup.Username));
            Assert.Equal(0, await CountUsersAsync("OnlyThis"));
        });
    }

    /// <summary>
    /// UI5 — one username, one identity. The second attempt is refused by the
    /// same 400 as everything else.
    /// </summary>
    [Fact]
    public async Task A_duplicate_username_is_refused()
    {
        await RunAsync(async (client, admin, _, cleanup) =>
        {
            var first = await PostAsync(client, admin.Carrier, Payload(cleanup));

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            cleanup.Created.Add(await UserIdAsync(first));

            var second = await PostAsync(
                client, admin.Carrier, Payload(cleanup, email: $"other-{cleanup.Email}"));

            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        });
    }

    /// <summary>
    /// AU6 — one active human per email address.
    /// </summary>
    [Fact]
    public async Task A_duplicate_email_is_refused()
    {
        await RunAsync(async (client, admin, _, cleanup) =>
        {
            var first = await PostAsync(client, admin.Carrier, Payload(cleanup));

            cleanup.Created.Add(await UserIdAsync(first));

            var second = await PostAsync(
                client, admin.Carrier,
                Payload(cleanup, username: $"other-{cleanup.Username}"));

            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        });
    }

    /// <summary>
    /// The document is opt-in and OFF by default, so the factory is told to
    /// publish it explicitly. An earlier version of this test used the default
    /// factory and returned early when the document was absent — which xUnit
    /// reports as passed having asserted nothing, the silent-skip this
    /// repository has been bitten by before (AGENTS.md section 3).
    /// </summary>
    [Fact]
    public async Task The_endpoint_appears_in_the_api_document()
    {
        await using var factory = new HostFactory("true");

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty("/api/users", out var users),
            "The OpenAPI document does not describe /api/users.");

        // The description must state the two things a caller cannot discover by
        // trying: that a refusal is 400 rather than 403, and that the created
        // account cannot yet be activated.
        var description = users.GetProperty("post").GetProperty("description")
            .GetString() ?? string.Empty;

        Assert.Contains("400", description, StringComparison.Ordinal);
        Assert.Contains("activated", description, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ harness

    private sealed record Caller(UserId UserId, string Carrier);

    private sealed class Cleanup
    {
        internal string Email { get; init; } = string.Empty;

        internal string Username { get; init; } = string.Empty;

        internal List<Guid> Created { get; } = [];
    }

    /// <summary>
    /// The fixture's identifiers are passed explicitly rather than held in
    /// ambient state. An earlier version used [ThreadStatic], which is silently
    /// wrong here: an async continuation can resume on a different thread, so
    /// the values vanished mid-test and every request went out with a null
    /// email — which the endpoint correctly rejected, making a harness bug look
    /// like an authorization result.
    /// </summary>
    private static object Payload(
        Cleanup cleanup, string? email = null, string? username = null)
        => new
        {
            FirstName = "Grace",
            LastName = "Hopper",
            DisplayName = "Grace Hopper",
            Email = email ?? cleanup.Email,
            InitialUsername = username ?? cleanup.Username,
        };

    private static async Task RunAsync(
        Func<HttpClient, Caller, Caller, Cleanup, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var discriminator = Guid.NewGuid().ToString("N")[..10];

        var cleanup = new Cleanup
        {
            Email = $"created-{discriminator}@example.test",
            Username = $"created-{discriminator}",
        };

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var admin = await EnsureCallerAsync(client, grantUserCreate: true);
        var plain = await EnsureCallerAsync(client, grantUserCreate: false);

        try
        {
            await body(client, admin, plain, cleanup);
        }
        finally
        {
            foreach (var created in cleanup.Created)
                await DeleteUserAsync(created);

            await DeleteByEmailAsync(cleanup.Email);
        }
    }

    /// <summary>
    /// A real signed-in caller: seeded pending, activated over HTTP, signed in
    /// over HTTP. The only difference between the two callers is one user_role
    /// row, so the tests attribute outcomes to the permission and nothing else.
    ///
    /// Seeding and activation happen once, on the run that first creates the
    /// caller; afterwards only the sign-in runs. Signing in each time rather
    /// than caching a carrier keeps what these tests exercise unchanged —
    /// every request still carries a token this suite obtained over HTTP.
    /// </summary>
    private static async Task<Caller> EnsureCallerAsync(
        HttpClient client, bool grantUserCreate)
    {
        var identifiers = grantUserCreate ? Administrator : Unprivileged;
        var label = grantUserCreate ? "endpoint-administrator" : "endpoint-unprivileged";
        var username = $"permanent-{label}";
        var userId = new UserId(identifiers.User);

        await using (var existing = CreateContext())
        {
            if (await existing.Set<User>().AnyAsync(x => x.Id == userId))
                return new Caller(userId, await SignInAsync(client, username));
        }

        await SeedCallerAsync(client, identifiers, label, username, grantUserCreate);

        return new Caller(userId, await SignInAsync(client, username));
    }

    private static async Task SeedCallerAsync(
        HttpClient client,
        (Guid User, Guid Identity) identifiers,
        string label,
        string username,
        bool grantUserCreate)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(identifiers.User), "Permanent", "Caller",
            $"Permanent {label}",
            $"permanent-{label}@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(identifiers.Identity), user.Id, ActorType.Human,
            username, now, User.SystemUserId);

        var tokenService = new UserTokenService();
        var tokenId = UserTokenId.New();
        var material = tokenService.Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash,
            now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, identity, token);

            if (grantUserCreate)
            {
                var roleId = await context.Set<Role>()
                    .Where(x => x.Code == UserAdministrator)
                    .Select(x => x.Id)
                    .SingleAsync();

                context.Add(
                    UserRole.Create(
                        UserRoleId.New(), user.Id, ActorType.Human, roleId,
                        ScopeType.Global, scopeId: null,
                        effectiveFrom: now, effectiveTo: null,
                        assignedAt: now, assignedBy: User.SystemUserId,
                        assignmentReason: "Create user endpoint tests",
                        createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var activation = await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password });

        activation.EnsureSuccessStatusCode();
    }

    private static async Task<string> SignInAsync(HttpClient client, string username)
    {
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await signIn.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? carrier, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(payload),
        };

        if (carrier is not null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", carrier);
        }

        return await client.SendAsync(request);
    }

    private static async Task<Guid> UserIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("userId").GetGuid();
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static async Task<long> CountIdentitiesAsync(string username)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_identity WHERE username = @username", connection);

        command.Parameters.AddWithValue("username", username);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountUsersAsync(string email)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE email = @email", connection);

        command.Parameters.AddWithValue("email", email);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarAsync(string sql, Guid? id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        if (id is not null)
            command.Parameters.AddWithValue("id", id.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task DeleteByEmailAsync(string email)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var find = new NpgsqlCommand(
            "SELECT id FROM app_user WHERE email = @email", connection);

        find.Parameters.AddWithValue("email", email);

        var ids = new List<Guid>();

        await using (var reader = await find.ExecuteReaderAsync())
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));

        foreach (var id in ids)
            await DeleteUserAsync(id);
    }

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
            DELETE FROM user_token WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            """
            DELETE FROM user_session WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            "DELETE FROM user_role WHERE user_id = @id",
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
