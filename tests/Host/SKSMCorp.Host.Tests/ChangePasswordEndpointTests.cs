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
/// CRD-C4 over HTTP, through real carriers: an activated account signed in
/// twice, so the two sessions can be told apart by what each carrier can still
/// do afterwards.
///
/// One pinned actor, for AuthenticationEndToEndTests' reason: activating,
/// signing in and changing a password are all audited, so the user and identity
/// are referenced by records nobody may delete. Their credential, history,
/// tokens and sessions are reset per run, which is what makes each test start
/// from a known password.
/// </summary>
public sealed class ChangePasswordEndpointTests
{
    private const string Password = "correct-horse-battery-staple";
    private const string Fresh = "a-brand-new-password-for-tests-7";

    private static readonly Guid PinnedUser =
        Guid.Parse("3f6c2a91-8b4e-4d27-a1c5-7e9d0b2f4a63");

    private static readonly Guid PinnedIdentity =
        Guid.Parse("5d8e1b37-2c9a-4f60-b8d4-1a7c3e9f0b25");

    private const string Username = "permanent-change-password";

    /// <summary>
    /// The contract end to end: 204 with no body, the new password signs in and
    /// the old does not, the other session is ended and this one is not.
    /// </summary>
    [Fact]
    public async Task A_change_keeps_this_session_and_ends_the_other()
    {
        await RunAsync(async client =>
        {
            var here = await SignInAsync(client, Password);
            var elsewhere = await SignInAsync(client, Password);

            Assert.NotNull(here);
            Assert.NotNull(elsewhere);

            var response = await ChangeAsync(client, here, new { CurrentPassword = Password, NewPassword = Fresh });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

            // The other carrier's session is revoked, so the pipeline refuses it.
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, elsewhere)).StatusCode);

            Assert.Null(await SignInAsync(client, Password));
            Assert.NotNull(await SignInAsync(client, Fresh));

            // This carrier still works — signing out is the proof, and last.
            Assert.Equal(HttpStatusCode.NoContent, (await SignOutAsync(client, here)).StatusCode);
        });
    }

    [Fact]
    public async Task A_wrong_current_password_is_a_400_and_changes_nothing()
    {
        await RunAsync(async client =>
        {
            var here = await SignInAsync(client, Password);

            var response = await ChangeAsync(
                client, here, new { CurrentPassword = "not-the-password-9", NewPassword = Fresh });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(Fresh, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            Assert.NotNull(await SignInAsync(client, Password));
        });
    }

    [Fact]
    public async Task A_missing_field_is_a_400_and_a_missing_carrier_is_a_401()
    {
        await RunAsync(async client =>
        {
            var here = await SignInAsync(client, Password);

            var missing = await ChangeAsync(client, here, new { CurrentPassword = Password });
            var anonymous = await ChangeAsync(client, carrier: null, new { CurrentPassword = Password, NewPassword = Fresh });

            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            Assert.NotNull(await SignInAsync(client, Password));
        });
    }

    // ------------------------------------------------------------ attempt limit

    /// <summary>
    /// Below the threshold the counted refusal is the SAME 400 as before, body
    /// and all — the uniform refusal the frozen CRD-C4 rule requires (L4).
    /// </summary>
    [Fact]
    public async Task Below_the_threshold_a_wrong_password_is_the_uniform_400()
    {
        await RunAsync(async client =>
        {
            var here = await SignInAsync(client, Password);

            var response = await ChangeAsync(
                client, here, new { CurrentPassword = "not-the-password-9", NewPassword = Fresh });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                """{"error":"The password could not be changed."}""",
                await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("Set-Cookie"));
        });
    }

    /// <summary>
    /// AC-2 and AC-3 over HTTP. The Nth wrong current password is a 401 that
    /// clears the carrier cookie, worded as every 401 is; that carrier is then
    /// refused. The account's other session still works, and the right
    /// password still signs in: no lockout (L1).
    /// </summary>
    [Fact]
    public async Task The_Nth_wrong_password_is_a_401_that_ends_only_this_session()
    {
        await RunAsync(async client =>
        {
            var here = await SignInAsync(client, Password);
            var elsewhere = await SignInAsync(client, Password);

            var limit = SecurityBaseline.Current.MaxFailedLoginAttempts;
            var wrong = new { CurrentPassword = "not-the-password-9", NewPassword = Fresh };

            for (var attempt = 1; attempt < limit; attempt++)
                Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(client, here, wrong)).StatusCode);

            var last = await ChangeAsync(client, here, wrong);

            Assert.Equal(HttpStatusCode.Unauthorized, last.StatusCode);
            Assert.Equal(
                """{"error":"Authentication is required."}""",
                await last.Content.ReadAsStringAsync());

            var cleared = Assert.Single(last.Headers.GetValues("Set-Cookie"));
            Assert.StartsWith("__Host-sksmcorp=;", cleared, StringComparison.Ordinal);
            Assert.Contains("expires=", cleared, StringComparison.OrdinalIgnoreCase);

            // This carrier is finished...
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, here)).StatusCode);

            // ...and nothing else is: the right password still signs in, and
            // the other session is still live (signing it out is the proof).
            Assert.NotNull(await SignInAsync(client, Password));
            Assert.Equal(HttpStatusCode.NoContent, (await SignOutAsync(client, elsewhere)).StatusCode);
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
                .TryGetProperty("/api/account/change-password", out var path),
            "The OpenAPI document does not describe the change-password endpoint.");

        var description = path.GetProperty("post").GetProperty("description").GetString() ?? "";

        Assert.Contains("204", description, StringComparison.Ordinal);
        Assert.Contains("other sessions", description, StringComparison.OrdinalIgnoreCase);

        // The attempt limit is documented where a client author will look.
        Assert.Contains("consecutive", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("401", description, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ harness

    private static async Task RunAsync(Func<HttpClient, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var activation = await SeedPendingAsync();

        try
        {
            (await client.PostAsJsonAsync(
                "/api/account/activate",
                new { Token = activation, NewPassword = Password }))
                .EnsureSuccessStatusCode();

            await body(client);
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>
    /// The pinned actor, pending activation, with a fresh activation token.
    /// Idempotent: the two pinned rows are created once and only reset after.
    /// </summary>
    private static async Task<string> SeedPendingAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await ResetAsync();

        await using (var connection = await TestDatabase.OpenAsync())
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO app_user
                    (id, actor_type, first_name, last_name, display_name, email,
                     status, created_at, created_by, updated_at, updated_by)
                VALUES
                    (@user, 'Human', 'Change', 'Password', 'Change Password',
                     'permanent-change-password@example.test',
                     'Active', @now, @system, @now, @system)
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO user_identity
                    (id, user_id, actor_type, identity_type, identity_provider,
                     subject_id, username, status, created_at, created_by)
                VALUES
                    (@identity, @user, 'Human', 'Local', 'Application',
                     @subject, @username, 'Active', @now, @system)
                ON CONFLICT (id) DO NOTHING;
                """, connection);

            command.Parameters.AddWithValue("user", PinnedUser);
            command.Parameters.AddWithValue("identity", PinnedIdentity);
            command.Parameters.AddWithValue("subject", PinnedIdentity.ToString());
            command.Parameters.AddWithValue("username", Username);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await command.ExecuteNonQueryAsync();
        }

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await using (var context = CreateContext())
        {
            context.Add(UserToken.Create(
                tokenId, new UserIdentityId(PinnedIdentity), TokenType.Activation,
                material.Hash, now, now.AddHours(72), User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        return material.PlainText;
    }

    /// <summary>Everything the actor accumulated, without touching the actor.</summary>
    private static async Task ResetAsync()
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
            command.Parameters.AddWithValue("id", PinnedIdentity);
            await command.ExecuteNonQueryAsync();
        }
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

    private static async Task<string?> SignInAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username, Password = password });

        if (response.StatusCode != HttpStatusCode.NoContent)
            return null;

        return IssuedCarrier.From(response);
    }

    private static Task<HttpResponseMessage> ChangeAsync(
        HttpClient client, string? carrier, object payload)
        => SendAsync(client, "/api/account/change-password", carrier, payload);

    private static Task<HttpResponseMessage> SignOutAsync(HttpClient client, string? carrier)
        => SendAsync(client, "/api/auth/sign-out", carrier, payload: null);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string path, string? carrier, object? payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (payload is not null)
            request.Content = JsonContent.Create(payload);

        if (carrier is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }
}
