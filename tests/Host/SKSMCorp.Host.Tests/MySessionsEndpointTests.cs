using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SKSMCorp.Host.Authentication;
using SKSMCorp.Host.Configuration;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// SES-Q2 over HTTP (docs/requirements.md, "SES-Q2 GetMySessions on the My
/// account page", MS-4 to MS-6).
///
/// The read is the caller's own: `current` follows the carrier presented,
/// nothing in the request can point it at another user, and the two existing
/// self-service ways of ending other sessions leave exactly the one in use.
///
/// Two pinned people with no role (5e520000…): "me" and "someone else". Their
/// credentials are recreated for each test, because one test changes the
/// password; the people and their records stay.
/// </summary>
public sealed class MySessionsEndpointTests
{
    private const string Password = "a-my-sessions-password-1";

    private const string NewPassword = "a-changed-my-sessions-password-2";

    private static readonly (Guid User, Guid Identity, string Username) Me =
        (Guid.Parse("5e520000-0000-4000-8000-000000000001"), Guid.Parse("5e520000-0000-4000-8000-000000000002"),
         "permanent-ses-q2-me");

    private static readonly (Guid User, Guid Identity, string Username) SomeoneElse =
        (Guid.Parse("5e520000-0000-4000-8000-000000000011"), Guid.Parse("5e520000-0000-4000-8000-000000000012"),
         "permanent-ses-q2-someone-else");

    private static readonly string[] Fields =
        ["sessionId", "createdAt", "lastActivityAt", "expiresAt", "idleExpiresAt", "ipAddress", "userAgent", "current"];

    /// <summary>MS-4 over HTTP.</summary>
    [Fact]
    public async Task Without_a_carrier_the_read_is_refused()
    {
        await using var factory = new HostFactory();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await GetAsync(factory.CreateClient(), carrier: null, "/api/account/sessions")).StatusCode);
    }

    /// <summary>MS-5: eight fields, current follows the carrier, and only the caller's own sessions.</summary>
    [Fact]
    public async Task Only_the_callers_own_sessions_are_read_and_current_follows_the_carrier()
    {
        await SeedAsync();
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var first = await SignInAsync(client, Me.Username, Password);
        var second = await SignInAsync(client, Me.Username, Password);
        var theirs = await SignInAsync(client, SomeoneElse.Username, Password);

        var asFirst = await ReadAsync(client, first);
        var asSecond = await ReadAsync(client, second);

        foreach (var row in asFirst)
            Assert.Equal(Fields.Order(), row.EnumerateObject().Select(x => x.Name).Order());

        Assert.Equal(new[] { SessionOf(first), SessionOf(second) }.Order(), Ids(asFirst).Order());
        Assert.Equal([SessionOf(first)], CurrentOf(asFirst));
        Assert.Equal([SessionOf(second)], CurrentOf(asSecond));
        Assert.DoesNotContain(SessionOf(theirs), Ids(asFirst));
    }

    /// <summary>MS-5: nothing in the request can make it read another user.</summary>
    [Fact]
    public async Task A_user_id_in_the_request_changes_nothing()
    {
        await SeedAsync();
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var mine = await SignInAsync(client, Me.Username, Password);
        var theirs = await SignInAsync(client, SomeoneElse.Username, Password);

        var response = await GetAsync(
            client, mine, $"/api/account/sessions?userId={SomeoneElse.User}&callerSessionId={SessionOf(theirs)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await RowsAsync(response);
        Assert.Equal([SessionOf(mine)], Ids(rows));
        Assert.Equal([SessionOf(mine)], CurrentOf(rows));
    }

    /// <summary>MS-6: after Sign out other sessions, exactly the one in use remains.</summary>
    [Fact]
    public async Task After_signing_out_other_sessions_exactly_this_one_remains()
    {
        await SeedAsync();
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var kept = await SignInAsync(client, Me.Username, Password);
        var other = await SignInAsync(client, Me.Username, Password);

        Assert.Equal(2, (await ReadAsync(client, kept)).Count);

        var signOut = await PostAsync(client, kept, "/api/account/sign-out-everywhere", new { KeepCurrentSession = true });
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        var rows = await ReadAsync(client, kept);
        Assert.Equal([SessionOf(kept)], Ids(rows));
        Assert.Equal([SessionOf(kept)], CurrentOf(rows));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, other, "/api/me")).StatusCode);
    }

    /// <summary>MS-6: a password change ends the other sessions (A5); exactly the one in use remains.</summary>
    [Fact]
    public async Task After_a_password_change_exactly_this_one_remains()
    {
        await SeedAsync();
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var kept = await SignInAsync(client, Me.Username, Password);
        await SignInAsync(client, Me.Username, Password);

        var change = await PostAsync(
            client, kept, "/api/account/change-password", new { CurrentPassword = Password, NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var rows = await ReadAsync(client, kept);
        Assert.Equal([SessionOf(kept)], Ids(rows));
        Assert.Equal([SessionOf(kept)], CurrentOf(rows));
    }

    // ------------------------------------------------------------- harness

    private static async Task<IReadOnlyList<JsonElement>> ReadAsync(HttpClient client, string carrier)
    {
        var response = await GetAsync(client, carrier, "/api/account/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await RowsAsync(response);
    }

    private static async Task<IReadOnlyList<JsonElement>> RowsAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessions").EnumerateArray().Select(x => x.Clone()).ToList();

    private static Guid[] Ids(IEnumerable<JsonElement> rows)
        => rows.Select(x => x.GetProperty("sessionId").GetGuid()).ToArray();

    private static Guid[] CurrentOf(IEnumerable<JsonElement> rows)
        => rows.Where(x => x.GetProperty("current").GetBoolean())
            .Select(x => x.GetProperty("sessionId").GetGuid())
            .ToArray();

    /// <summary>The session a carrier names, as the host's own AccessCarrier reads it.</summary>
    private static Guid SessionOf(string carrier)
        => new AccessCarrier(
                SigningKeyRing.Load(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                        [
                            new KeyValuePair<string, string?>(SigningKeyRing.CurrentKeySetting, HostFactory.PrimaryKeyId),
                            new KeyValuePair<string, string?>("SKSMCORP_SIGNING_KEY_V1", HostFactory.PrimaryKey),
                        ])
                        .Build()))
            .Verify(carrier)?.Value
           ?? throw new InvalidOperationException("The carrier did not verify.");

    private static async Task<string> SignInAsync(HttpClient client, string username, string password)
    {
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password = password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string? carrier, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string carrier, string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    /// <summary>Both people once, then a fresh credential and no sessions for each.</summary>
    private static async Task SeedAsync()
    {
        await TestDatabase.EnsureProvisionedAsync();

        foreach (var person in new[] { Me, SomeoneElse })
            await SeedPersonAsync(person);
    }

    private static async Task SeedPersonAsync((Guid User, Guid Identity, string Username) person)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using (var reset = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@user, 'Human', 'My', 'Sessions', @username, @email, 'Active', now(), @system, now(), @system)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@identity, @user, 'Human', 'Local', 'Application', @identity, @username, 'Active', now(), @system)
            ON CONFLICT (id) DO NOTHING;

            DELETE FROM password_history WHERE user_identity_id = @identity;
            DELETE FROM user_session WHERE user_identity_id = @identity;
            DELETE FROM credential WHERE user_identity_id = @identity;
            """, connection))
        {
            reset.Parameters.AddWithValue("user", person.User);
            reset.Parameters.AddWithValue("identity", person.Identity);
            reset.Parameters.AddWithValue("username", person.Username);
            reset.Parameters.AddWithValue("email", $"{person.Username}@example.test");
            reset.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await reset.ExecuteNonQueryAsync();
        }

        var hashed = new PasswordHasher().Hash(Password);

        await using var credential = new NpgsqlCommand(
            """
            INSERT INTO credential
                (id, user_identity_id, identity_type, password_hash, password_algorithm,
                 password_changed_at, must_change_password, failed_attempt_count, locked_until,
                 created_at, created_by)
            VALUES
                (gen_random_uuid(), @identity, 'Local', @hash, @algorithm,
                 now() - interval '30 days', false, 0, NULL, now(), @system)
            """, connection);

        credential.Parameters.AddWithValue("identity", person.Identity);
        credential.Parameters.AddWithValue("hash", hashed.Hash);
        credential.Parameters.AddWithValue("algorithm", hashed.Algorithm);
        credential.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await credential.ExecuteNonQueryAsync();
    }
}
