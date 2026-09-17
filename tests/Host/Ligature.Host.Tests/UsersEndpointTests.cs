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
/// USR-Q1 over HTTP — GET /api/users.
///
/// What the endpoint owns: the wire contract (exactly four response members,
/// exactly three row members), parameter binding (malformed values refused
/// with the host's error body, unknown parameters ignored), the refusal
/// statuses, and P6 — that a row's userId is what the user-scoped routes
/// accept. The handler's arithmetic and the reader's collation are proven in
/// their own suites; this suite proves they are what the endpoint serves.
///
/// Three callers, seeded once and never removed, with identifiers of their
/// own: a user administrator, an access reviewer — who holds user.read and
/// none of the action permissions, so the list must not require more — and a
/// caller with no role at all.
/// </summary>
public sealed class UsersEndpointTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly Guid SystemUser =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("d1a50000-0000-4000-8000-0000000051a1"),
         Guid.Parse("d1a50000-0000-4000-8000-0000000051a2"));

    private static readonly (Guid User, Guid Identity) Reviewer =
        (Guid.Parse("d1a50000-0000-4000-8000-0000000052a1"),
         Guid.Parse("d1a50000-0000-4000-8000-0000000052a2"));

    private static readonly (Guid User, Guid Identity) Unprivileged =
        (Guid.Parse("d1a50000-0000-4000-8000-0000000053a1"),
         Guid.Parse("d1a50000-0000-4000-8000-0000000053a2"));

    // ------------------------------------------------------------ the wire

    [Fact]
    public async Task A_request_without_parameters_returns_page_1_of_25()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Administrator, "/api/users");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(25, body.RootElement.GetProperty("pageSize").GetInt32());
            Assert.True(body.RootElement.GetProperty("users").GetArrayLength() <= 25);
        });
    }

    [Fact]
    public async Task The_response_has_exactly_users_page_page_size_and_has_more()
    {
        await RunAsync(async (client, callers) =>
        {
            using var body = await JsonAsync(await GetAsync(client, callers.Administrator, "/api/users"));

            Assert.Equal(
                ["hasMore", "page", "pageSize", "users"],
                body.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));

            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("users").ValueKind);
        });
    }

    /// <summary>P1 and P3, on every row the caller can reach.</summary>
    [Fact]
    public async Task Every_row_has_exactly_user_id_display_name_and_email()
    {
        await RunAsync(async (client, callers) =>
        {
            var rows = await ReadAllRowsAsync(client, callers.Administrator);

            Assert.NotEmpty(rows);

            Assert.All(rows, row => Assert.Equal(
                ["displayName", "email", "userId"],
                row.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)));
        });
    }

    /// <summary>P2, and the complete set of human users (P14).</summary>
    [Fact]
    public async Task The_list_is_every_human_user_and_never_the_system_user()
    {
        await RunAsync(async (client, callers) =>
        {
            var ids = (await ReadAllRowsAsync(client, callers.Administrator))
                .Select(x => x.GetProperty("userId").GetGuid())
                .ToList();

            Assert.DoesNotContain(SystemUser, ids);
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Equal((await HumanIdsAsync()).Order(), ids.Order());
        });
    }

    /// <summary>
    /// The reader's collation, as served. "de la Cruz" / "Delacroix" is ordered
    /// differently by this database's default collation.
    /// </summary>
    [Fact]
    public async Task Rows_are_served_in_the_contract_order()
    {
        await RunAsync(async (client, callers) =>
        {
            var marker = Guid.NewGuid().ToString("N")[..10];
            string[] expected = ["adam", "Bob", "de la Cruz", "Delacroix"];

            var seeded = new List<Guid>();

            try
            {
                foreach (var name in expected.Reverse())
                    seeded.Add(await SeedListedUserAsync($"{name} {marker}"));

                var order = (await ReadAllRowsAsync(client, callers.Administrator))
                    .Select(x => x.GetProperty("displayName").GetString()!)
                    .Where(x => x.EndsWith(marker, StringComparison.Ordinal))
                    .Select(x => x[..^(marker.Length + 1)]);

                Assert.Equal(expected, order);
            }
            finally
            {
                foreach (var id in seeded)
                    await DeleteUserAsync(id);
            }
        });
    }

    /// <summary>
    /// The column is nullable and nothing in the database requires a human to
    /// have an address, so a row without one is listed with an explicit null
    /// rather than hidden or given a placeholder.
    /// </summary>
    [Fact]
    public async Task A_user_without_an_email_is_listed_with_a_null_email()
    {
        await RunAsync(async (client, callers) =>
        {
            var marker = Guid.NewGuid().ToString("N")[..10];
            var id = await SeedListedUserAsync($"No Address {marker}", email: null);

            try
            {
                var row = Assert.Single(
                    await ReadAllRowsAsync(client, callers.Administrator),
                    x => x.GetProperty("userId").GetGuid() == id);

                Assert.Equal(JsonValueKind.Null, row.GetProperty("email").ValueKind);
            }
            finally
            {
                await DeleteUserAsync(id);
            }
        });
    }

    [Fact]
    public async Task The_largest_page_size_is_accepted()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Administrator, "/api/users?page=1&pageSize=100");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = await JsonAsync(response);

            Assert.Equal(100, body.RootElement.GetProperty("pageSize").GetInt32());
        });
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_with_no_further_page()
    {
        await RunAsync(async (client, callers) =>
        {
            foreach (var page in new[] { 1000000, int.MaxValue })
            {
                var response = await GetAsync(
                    client, callers.Administrator, $"/api/users?page={page}&pageSize=100");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                using var body = await JsonAsync(response);

                Assert.Equal(0, body.RootElement.GetProperty("users").GetArrayLength());
                Assert.False(body.RootElement.GetProperty("hasMore").GetBoolean());
                Assert.Equal(page, body.RootElement.GetProperty("page").GetInt32());
            }
        });
    }

    // ------------------------------------------------------------ parameters

    [Theory]
    [InlineData("sortBy=email")]
    [InlineData("sortBy=email&sortDirection=desc")]
    [InlineData("whatever=abc")]
    [InlineData("email=permanent")]
    [InlineData("search=Permanent&filter=status")]
    public async Task Unknown_parameters_are_ignored(string unknown)
    {
        await RunAsync(async (client, callers) =>
        {
            var plain = await GetAsync(client, callers.Administrator, "/api/users?pageSize=100");
            var decorated = await GetAsync(client, callers.Administrator, $"/api/users?pageSize=100&{unknown}");

            Assert.Equal(HttpStatusCode.OK, decorated.StatusCode);
            Assert.Equal(
                await plain.Content.ReadAsStringAsync(),
                await decorated.Content.ReadAsStringAsync());
        });
    }

    [Theory]
    [InlineData("page=abc")]
    [InlineData("pageSize=abc")]
    [InlineData("page=1.5")]
    [InlineData("pageSize=2e1")]
    [InlineData("page=1&page=2")]
    [InlineData("pageSize=10&pageSize=20")]
    [InlineData("page=2147483648")]
    [InlineData("pageSize=2147483648")]
    [InlineData("page=-2147483649")]
    public async Task A_malformed_recognised_parameter_is_refused_with_the_error_body(string query)
    {
        await RunAsync(async (client, callers) =>
        {
            await AssertInvalidRequestAsync(
                await GetAsync(client, callers.Administrator, $"/api/users?{query}"));
        });
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-5")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=1000")]
    public async Task An_out_of_range_recognised_parameter_is_refused_with_the_error_body(string query)
    {
        await RunAsync(async (client, callers) =>
        {
            await AssertInvalidRequestAsync(
                await GetAsync(client, callers.Administrator, $"/api/users?{query}"));
        });
    }

    // ------------------------------------------------------------ refusals

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_with_401()
    {
        await RunAsync(async (client, _) =>
        {
            var response = await GetAsync(client, carrier: null, "/api/users");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("users", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_caller_without_user_read_is_refused_with_400()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Unprivileged, "/api/users");

            await AssertInvalidRequestAsync(response);
            Assert.DoesNotContain("\"users\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Authorization precedes range validation: a caller without user.read
    /// receives the same refusal whatever in-range or out-of-range values it
    /// sent, and so learns nothing about which are valid.
    /// </summary>
    [Fact]
    public async Task A_caller_without_user_read_learns_nothing_about_parameter_ranges()
    {
        await RunAsync(async (client, callers) =>
        {
            var plain = await GetAsync(client, callers.Unprivileged, "/api/users");
            var outOfRange = await GetAsync(client, callers.Unprivileged, "/api/users?page=0&pageSize=1000");

            Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
            Assert.Equal(
                await plain.Content.ReadAsStringAsync(),
                await outOfRange.Content.ReadAsStringAsync());
        });
    }

    /// <summary>
    /// user.read is the whole requirement. The access reviewer holds it and no
    /// permission any row action needs.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_only_user_read_among_user_permissions_can_list()
    {
        await RunAsync(async (client, callers) =>
        {
            var response = await GetAsync(client, callers.Reviewer, "/api/users");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    // ------------------------------------------------------------ P6

    /// <summary>
    /// P6 — the userId a row carries is accepted, unchanged, by both
    /// user-scoped routes and addresses the user the row describes. The target
    /// is eligible for both, so success is the evidence; a route that did not
    /// resolve the identifier would refuse an unknown user instead.
    /// </summary>
    [Fact]
    public async Task A_rows_user_id_is_accepted_by_the_user_scoped_routes()
    {
        await RunAsync(async (client, callers) =>
        {
            var marker = Guid.NewGuid().ToString("N")[..10];
            var target = await SeedEligibleTargetAsync($"Row Target {marker}");

            try
            {
                var row = Assert.Single(
                    await ReadAllRowsAsync(client, callers.Administrator),
                    x => x.GetProperty("displayName").GetString() == $"Row Target {marker}");

                var userId = row.GetProperty("userId").GetString()!;

                var reset = await PostAsync(
                    client, callers.Administrator, $"/api/users/{userId}/password-reset",
                    new { Reason = "USR-Q1 P6." });

                var signOut = await PostAsync(
                    client, callers.Administrator, $"/api/users/{userId}/sign-out-everywhere",
                    new { Reason = "USR-Q1 P6." });

                Assert.Equal(HttpStatusCode.Accepted, reset.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
                Assert.Equal(target, Guid.Parse(userId));
            }
            finally
            {
                await DeleteUserAsync(target);
            }
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
            document.RootElement.GetProperty("paths").TryGetProperty("/api/users", out var path)
                && path.TryGetProperty("get", out _),
            "The OpenAPI document does not describe GET /api/users.");
    }

    // ------------------------------------------------------------ harness

    private sealed record Callers(string Administrator, string Reviewer, string Unprivileged);

    private static async Task RunAsync(Func<HttpClient, Callers, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var callers = new Callers(
            await EnsureCallerAsync(client, Administrator, "users-list-administrator", "user-administrator"),
            await EnsureCallerAsync(client, Reviewer, "users-list-reviewer", "access-reviewer"),
            await EnsureCallerAsync(client, Unprivileged, "users-list-unprivileged", role: null));

        await body(client, callers);
    }

    private static async Task AssertInvalidRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = await JsonAsync(response);

        Assert.False(
            string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()),
            "An invalid request must carry the host's error body.");
    }

    private static async Task<List<JsonElement>> ReadAllRowsAsync(HttpClient client, string carrier)
    {
        var rows = new List<JsonElement>();

        for (var page = 1; ; page++)
        {
            var response = await GetAsync(client, carrier, $"/api/users?page={page}&pageSize=100");

            response.EnsureSuccessStatusCode();

            using var body = await JsonAsync(response);

            rows.AddRange(body.RootElement.GetProperty("users").EnumerateArray().Select(x => x.Clone()));

            if (!body.RootElement.GetProperty("hasMore").GetBoolean())
                return rows;
        }
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string? carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<string> EnsureCallerAsync(
        HttpClient client, (Guid User, Guid Identity) identifiers, string label, string? role)
    {
        var username = $"permanent-{label}";
        var userId = new UserId(identifiers.User);

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == userId))
                await SeedCallerAsync(client, identifiers, label, username, role);
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
        string? role)
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

            if (role is not null)
            {
                var roleId = await context.Set<Role>()
                    .Where(x => x.Code == role)
                    .Select(x => x.Id)
                    .SingleAsync();

                context.Add(
                    UserRole.Create(
                        UserRoleId.New(), user.Id, ActorType.Human, roleId,
                        ScopeType.Global, scopeId: null,
                        effectiveFrom: now, effectiveTo: null,
                        assignedAt: now, assignedBy: User.SystemUserId,
                        assignmentReason: "USR-Q1 user list endpoint tests",
                        createdAt: now, createdBy: User.SystemUserId));
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>A human user with no identity: listed, and eligible for nothing.</summary>
    private static async Task<Guid> SeedListedUserAsync(string displayName, string? email = "")
    {
        var userId = Guid.NewGuid();

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@id, 'Human', 'Listed', 'User', @display, @email, 'Active',
                 now(), @system, now(), @system)
            """, connection);

        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("display", displayName);
        command.Parameters.AddWithValue(
            "email",
            email is null ? DBNull.Value : $"usr-q1-{userId:N}@example.test");
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();

        return userId;
    }

    /// <summary>
    /// Eligible for both user-scoped routes: active human, one active local
    /// identity, a credential.
    /// </summary>
    private static async Task<Guid> SeedEligibleTargetAsync(string displayName)
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var hashed = new PasswordHasher().Hash("the-target-password-1");

        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@user, 'Human', 'Row', 'Target', @display, @email, 'Active',
                 now(), @system, now(), @system);

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@identity, @user, 'Human', 'Local', 'Application',
                 @subject, @username, 'Active', now(), @system);

            INSERT INTO credential
                (id, user_identity_id, identity_type, password_hash,
                 password_algorithm, password_changed_at, must_change_password,
                 failed_attempt_count, locked_until, created_at, created_by)
            VALUES
                (@credential, @identity, 'Local', @hash,
                 @algorithm, now(), false, 0, NULL, now(), @system);
            """, connection);

        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("identity", identityId);
        command.Parameters.AddWithValue("credential", Guid.NewGuid());
        command.Parameters.AddWithValue("subject", identityId.ToString());
        command.Parameters.AddWithValue("username", $"usr-q1-target-{userId:N}");
        command.Parameters.AddWithValue("display", displayName);
        command.Parameters.AddWithValue("email", $"usr-q1-target-{userId:N}@example.test");
        command.Parameters.AddWithValue("hash", hashed.Hash);
        command.Parameters.AddWithValue("algorithm", hashed.Algorithm);
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();

        return userId;
    }

    private static async Task<List<Guid>> HumanIdsAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM app_user WHERE actor_type = 'Human'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<Guid>();

        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));

        return ids;
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

    /// <summary>
    /// A listed user or P6 target is the SUBJECT of anything written about it,
    /// never an actor, so no key prevents removing it. CRD-C5's reset leaves a
    /// token and a notification behind, removed first.
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
