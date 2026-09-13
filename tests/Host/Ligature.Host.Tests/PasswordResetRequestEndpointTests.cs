using System.Net;
using System.Net.Http.Json;
using Ligature.Platform.Domain.Users;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// CRD-C2 over HTTP, and specifically the one property the command exists to
/// protect: the answer must not depend on what was typed.
///
/// The frozen failure mode is "MUST return an identical response whether or
/// not the account exists — otherwise this is an account-enumeration oracle",
/// and the test obligation names three inputs: an unknown address, an external
/// identity, and a known local identity. Only the third may write anything.
///
/// ASSERTED ON THE RESPONSE BYTES, not the status code. A matching 200 that
/// differed in body shape, field order or wording would leak exactly as
/// effectively as a 404, and would be far easier to introduce by accident —
/// a helpful "no account found" message is the obvious thing a later
/// contributor adds.
///
/// WHAT THIS CANNOT ASSERT: timing. D-NOTIF-03 accepts a residual timing
/// difference between the branches because rate limiting compensates for it,
/// and rate limiting is not implemented (docs/requirements.md). No test here
/// pretends otherwise.
///
/// Fixed identifiers, seeded idempotently, for the reason CreateUserEndpointTests
/// explains: these accounts become audit subjects and cannot be cleaned up.
/// Re-running is not merely tolerated but useful — the second run's request
/// supersedes the first run's token, which is UT5 doing its job.
/// </summary>
public sealed class PasswordResetRequestEndpointTests
{
    private const string Endpoint = "/api/account/password-reset-request";

    private static readonly Guid LocalUser =
        Guid.Parse("c0000000-0000-4000-8000-000000000001");

    private static readonly Guid LocalIdentity =
        Guid.Parse("c0000000-0000-4000-8000-000000000002");

    private static readonly Guid ExternalUser =
        Guid.Parse("c0000000-0000-4000-8000-000000000003");

    private static readonly Guid ExternalIdentity =
        Guid.Parse("c0000000-0000-4000-8000-000000000004");

    /// <summary>
    /// Active, local, human, with an address — and no credential. Created but
    /// never activated. Since CRD-C3 it is not eligible for a reset.
    /// </summary>
    private static readonly Guid PendingUser =
        Guid.Parse("c0000000-0000-4000-8000-000000000005");

    private static readonly Guid PendingIdentity =
        Guid.Parse("c0000000-0000-4000-8000-000000000006");

    private const string PendingEmail = "reset-pending@example.test";

    private const string LocalEmail = "reset-local@example.test";
    private const string LocalUsername = "reset-local";
    private const string ExternalEmail = "reset-external@example.test";

    [Fact]
    public async Task Every_input_gets_a_byte_identical_response()
    {
        await SeedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        // The known local account LAST, so that if the responses did differ,
        // the difference could not be blamed on ordering or on a warmed cache.
        var unknown = await PostAsync(client, "nobody@example.test");
        var external = await PostAsync(client, ExternalEmail);
        var neverActivated = await PostAsync(client, PendingEmail);
        var known = await PostAsync(client, LocalEmail);
        var knownByUsername = await PostAsync(client, LocalUsername);
        var blank = await PostAsync(client, "");

        foreach (var response in new[] { unknown, external, neverActivated, known, knownByUsername, blank })
            Assert.Equal(HttpStatusCode.OK, response.Status);

        // The bytes themselves. Not a shape, not a field count.
        Assert.Equal(unknown.Body, external.Body);
        Assert.Equal(unknown.Body, neverActivated.Body);
        Assert.Equal(unknown.Body, known.Body);
        Assert.Equal(unknown.Body, knownByUsername.Body);
        Assert.Equal(unknown.Body, blank.Body);

        // And nothing in the body names the input, which is the other way a
        // uniform-looking response leaks.
        Assert.DoesNotContain(LocalEmail, unknown.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nobody@example.test", unknown.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_the_known_local_account_is_issued_a_token()
    {
        await SeedAsync();

        await using var factory = new HostFactory();
        var client = factory.CreateClient();

        var localBefore = await CountOpenResetTokensAsync(LocalIdentity);
        var externalBefore = await CountOpenResetTokensAsync(ExternalIdentity);

        await PostAsync(client, "nobody@example.test");
        await PostAsync(client, ExternalEmail);

        Assert.Equal(localBefore, await CountOpenResetTokensAsync(LocalIdentity));
        Assert.Equal(externalBefore, await CountOpenResetTokensAsync(ExternalIdentity));

        // The external identity gets nothing at all, not even on its own
        // request: an external account resets at its provider, and issuing a
        // local token for it would be a second credential path nobody asked
        // for.
        Assert.Equal(0, externalBefore);

        // Never activated: no credential, so no reset token either — and the
        // response above was byte-identical to an unknown address's.
        await PostAsync(client, PendingEmail);
        Assert.Equal(0, await CountOpenResetTokensAsync(PendingIdentity));

        await PostAsync(client, LocalEmail);

        // Exactly one open token, whatever the previous runs left behind —
        // UT5 superseded them and UT4 would have refused a second.
        Assert.Equal(1, await CountOpenResetTokensAsync(LocalIdentity));
    }

    // ------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(
        HttpClient client, string emailOrUsername)
    {
        var response = await client.PostAsJsonAsync(
            Endpoint, new { EmailOrUsername = emailOrUsername });

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<int> CountOpenResetTokensAsync(Guid identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM user_token
            WHERE user_identity_id = @id
              AND token_type = 'PasswordReset'
              AND used_at IS NULL
              AND invalidated_at IS NULL
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Idempotent: ON CONFLICT DO NOTHING, so the first run creates the two
    /// accounts and every later run finds them.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@localUser, 'Human', 'Reset', 'Local', 'Reset Local', @localEmail,
                 'Active', now(), @system, now(), @system),
                (@externalUser, 'Human', 'Reset', 'External', 'Reset External',
                 @externalEmail, 'Active', now(), @system, now(), @system),
                (@pendingUser, 'Human', 'Reset', 'Pending', 'Reset Pending',
                 @pendingEmail, 'Active', now(), @system, now(), @system)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@localIdentity, @localUser, 'Human', 'Local', 'Application',
                 @localIdentity, @localUsername, 'Active', now(), @system),
                (@externalIdentity, @externalUser, 'Human', 'External', 'Okta',
                 @externalIdentity, NULL, 'Active', now(), @system),
                (@pendingIdentity, @pendingUser, 'Human', 'Local', 'Application',
                 @pendingIdentity, 'reset-pending', 'Active', now(), @system)
            ON CONFLICT (id) DO NOTHING;

            -- Eligibility requires a credential since CRD-C3. The known local
            -- account gets one; the pending account deliberately does not.
            -- No conflict target: CR1's unique index and the primary key both
            -- make a re-run a no-op.
            INSERT INTO credential
                (id, user_identity_id, identity_type, password_hash,
                 password_algorithm, password_changed_at, must_change_password,
                 failed_attempt_count, locked_until, created_at, created_by)
            VALUES
                ('c0000000-0000-4000-8000-000000000007', @localIdentity, 'Local',
                 'a-hash', 'pbkdf2-sha256-v1', now(), false, 0, NULL, now(), @system)
            ON CONFLICT DO NOTHING;
            """, connection);

        command.Parameters.AddWithValue("localUser", LocalUser);
        command.Parameters.AddWithValue("localIdentity", LocalIdentity);
        command.Parameters.AddWithValue("localEmail", LocalEmail);
        command.Parameters.AddWithValue("localUsername", LocalUsername);
        command.Parameters.AddWithValue("externalUser", ExternalUser);
        command.Parameters.AddWithValue("externalIdentity", ExternalIdentity);
        command.Parameters.AddWithValue("externalEmail", ExternalEmail);
        command.Parameters.AddWithValue("pendingUser", PendingUser);
        command.Parameters.AddWithValue("pendingIdentity", PendingIdentity);
        command.Parameters.AddWithValue("pendingEmail", PendingEmail);
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();
    }
}
