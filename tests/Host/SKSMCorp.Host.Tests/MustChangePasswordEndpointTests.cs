using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// SES-C1 with MustChangePassword outstanding, over HTTP (docs/requirements.md,
/// "SES-C1 — enforcing MustChangePassword at sign-in", MP-7 to MP-9).
///
/// The refusal must be indistinguishable from a wrong password (MC2), and the
/// two ways out must work end to end: a reset link (CRD-C3), and a password
/// change from a session that existed before the reset (CRD-C4, MC5).
///
/// One pinned account (5ec1a000…), for the reason every host suite pins its
/// actors: once it has signed in it is the subject of audit records and cannot
/// be removed. Its credential is recreated for each test, so every test starts
/// from the same password with the flag set as the test needs.
/// </summary>
public sealed class MustChangePasswordEndpointTests
{
    private static readonly Guid UserId = Guid.Parse("5ec1a000-0000-4000-8000-000000000001");

    private static readonly Guid IdentityId = Guid.Parse("5ec1a000-0000-4000-8000-000000000002");

    private const string Username = "permanent-must-change-password";

    private const string OldPassword = "the-password-on-file-1";

    private const string NewPassword = "an-entirely-new-password-9";

    /// <summary>MP-7: the refusal is byte-identical to a wrong password's, and sets no cookie.</summary>
    [Fact]
    public async Task A_correct_password_with_a_change_outstanding_looks_exactly_like_a_wrong_one()
    {
        await SeedAsync(mustChangePassword: true);
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var outstanding = await SignInAsync(client, OldPassword);
        var wrong = await SignInAsync(client, "not-the-password-at-all");

        Assert.Equal(HttpStatusCode.Unauthorized, outstanding.StatusCode);
        Assert.Equal(wrong.StatusCode, outstanding.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await outstanding.Content.ReadAsStringAsync());
        Assert.True(IssuedCarrier.IsAbsent(outstanding));
        Assert.Equal(0, await SessionCountAsync());
    }

    /// <summary>MP-8: the way out through a reset link, end to end.</summary>
    [Fact]
    public async Task A_reset_link_clears_the_change_and_the_new_password_signs_in()
    {
        await SeedAsync(mustChangePassword: true);
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, OldPassword)).StatusCode);

        var token = await IssueResetTokenAsync();

        var reset = await client.PostAsJsonAsync(
            "/api/account/reset-password", new { Token = token, NewPassword });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False(await MustChangePasswordAsync());

        var before = await SignInSucceededCountAsync();
        var signedIn = await SignInAsync(client, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(before + 1, await SignInSucceededCountAsync());

        // And normal access works with the carrier it issued.
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(client, IssuedCarrier.From(signedIn))).StatusCode);
    }

    /// <summary>
    /// MP-9 (MC5, MC6): a session established before the reset stays valid —
    /// CRD-C5 revokes nothing — and a password change through it clears the flag.
    /// </summary>
    [Fact]
    public async Task A_session_from_before_the_reset_still_works_and_can_clear_the_change()
    {
        await SeedAsync(mustChangePassword: false);
        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var signedIn = await SignInAsync(client, OldPassword);
        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        var carrier = IssuedCarrier.From(signedIn);

        await SetMustChangePasswordAsync(true);

        Assert.Equal(HttpStatusCode.OK, (await MeAsync(client, carrier)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, OldPassword)).StatusCode);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/account/change-password")
        {
            Content = JsonContent.Create(new { CurrentPassword = OldPassword, NewPassword }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(change)).StatusCode);
        Assert.False(await MustChangePasswordAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(client, NewPassword)).StatusCode);
    }

    // ------------------------------------------------------------- harness

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string password)
        => client.PostAsJsonAsync("/api/auth/sign-in", new { Username, Password = password });

    private static Task<HttpResponseMessage> MeAsync(HttpClient client, string carrier)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);
        return client.SendAsync(request);
    }

    /// <summary>
    /// The person once, then a fresh credential for this test: the old
    /// password on file, and the flag as the test needs it. Sessions and
    /// history go with the old credential; the person and their records stay.
    /// </summary>
    private static async Task SeedAsync(bool mustChangePassword)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var connection = await TestDatabase.OpenAsync();

        await using (var person = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@user, 'Human', 'Must', 'Change', 'Must Change',
                 'permanent-must-change-password@example.test', 'Active',
                 now(), @system, now(), @system)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@identity, @user, 'Human', 'Local', 'Application',
                 @identity, @username, 'Active', now(), @system)
            ON CONFLICT (id) DO NOTHING;

            DELETE FROM password_history WHERE user_identity_id = @identity;
            DELETE FROM user_session WHERE user_identity_id = @identity;
            DELETE FROM credential WHERE user_identity_id = @identity;
            UPDATE user_token SET invalidated_at = now()
            WHERE user_identity_id = @identity AND used_at IS NULL AND invalidated_at IS NULL;
            """, connection))
        {
            person.Parameters.AddWithValue("user", UserId);
            person.Parameters.AddWithValue("identity", IdentityId);
            person.Parameters.AddWithValue("username", Username);
            person.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await person.ExecuteNonQueryAsync();
        }

        var hashed = new PasswordHasher().Hash(OldPassword);

        await using var credential = new NpgsqlCommand(
            """
            INSERT INTO credential
                (id, user_identity_id, identity_type, password_hash,
                 password_algorithm, password_changed_at, must_change_password,
                 failed_attempt_count, locked_until, created_at, created_by)
            VALUES
                (gen_random_uuid(), @identity, 'Local', @hash, @algorithm,
                 now() - interval '30 days', @flag, 0, NULL, now(), @system)
            """, connection);

        credential.Parameters.AddWithValue("identity", IdentityId);
        credential.Parameters.AddWithValue("hash", hashed.Hash);
        credential.Parameters.AddWithValue("algorithm", hashed.Algorithm);
        credential.Parameters.AddWithValue("flag", mustChangePassword);
        credential.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await credential.ExecuteNonQueryAsync();
    }

    /// <summary>What CRD-C5 would have emailed: an open PasswordReset token, and its plaintext.</summary>
    private static async Task<string> IssueResetTokenAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await using var context = new SKSMCorpDbContext(
            new DbContextOptionsBuilder<SKSMCorpDbContext>()
                .UseNpgsql(TestDatabase.ConnectionString)
                .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
                .Options);

        context.Add(UserToken.Create(
            tokenId, new UserIdentityId(IdentityId), TokenType.PasswordReset, material.Hash,
            now, now.AddHours(1), User.SystemUserId));

        await context.SaveChangesAsync(CancellationToken.None);

        return material.PlainText;
    }

    private static async Task SetMustChangePasswordAsync(bool value)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE credential SET must_change_password = @value WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("id", IdentityId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> MustChangePasswordAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT must_change_password FROM credential WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", IdentityId);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> SessionCountAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_session WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", IdentityId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> SignInSucceededCountAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'SignInSucceeded' AND e.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", IdentityId);

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
