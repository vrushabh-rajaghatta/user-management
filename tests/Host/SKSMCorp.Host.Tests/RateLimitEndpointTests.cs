using System.Net;
using SKSMCorp.Platform.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static SKSMCorp.Host.Tests.RateLimitHarness;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// Behaviour 11 over HTTP, against the real pipeline and PostgreSQL
/// (docs/requirements.md, "Behaviour 11 — rate limiting the anonymous
/// commands"). Each test builds its own host, so each starts with empty
/// counters (RL-21) — and requests that set no address are not IP-limited
/// (RL-19), which is why the rest of this suite is unaffected by the limits.
///
/// Rate limiting measures REQUEST VOLUME, not authentication correctness. The
/// tests below are written to stop it becoming a failed-login counter: they
/// count successes, count unknown accounts, and prove a refusal leaves the
/// authentication and lockout state exactly where it was.
/// </summary>
public sealed class RateLimitEndpointTests
{
    // ------------------------------------------------------------- sign-in

    /// <summary>RL-1, RL-14, RL-15.</summary>
    [Fact]
    public async Task The_eleventh_sign_in_for_one_username_is_refused_before_it_runs()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        var username = Fresh("rl-nobody");

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(factory, username)).Status);

        var refused = await SignInAsync(factory, username);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);

        Assert.NotNull(refused.RetryAfter);
        var seconds = int.Parse(refused.RetryAfter!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, 15 * 60);

        // The 11th reached no handler: ten attempts recorded, not eleven.
        Assert.Equal(10, await SignInFailedCountAsync(username));
    }

    /// <summary>RL-2: 31 different usernames, one address.</summary>
    [Fact]
    public async Task The_thirty_first_sign_in_from_one_address_is_refused()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SignInAsync(factory, Fresh("rl-spray"), peer: "203.0.113.21")).Status);
        }

        var refused = await SignInAsync(factory, Fresh("rl-spray"), peer: "203.0.113.21");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
        Assert.InRange(int.Parse(refused.RetryAfter!, System.Globalization.CultureInfo.InvariantCulture), 1, 60);

        // Another address is untouched.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SignInAsync(factory, Fresh("rl-spray"), peer: "203.0.113.22")).Status);
    }

    /// <summary>RL-5: successes count.</summary>
    [Fact]
    public async Task Successful_sign_ins_consume_the_username_budget()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await SignInAsync(factory, SignerUsername, Password)).Status);
        }

        var refused = await SignInAsync(factory, SignerUsername, Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
        Assert.Equal(10, await SignerSessionCountAsync());
    }

    /// <summary>RL-8: a refusal is not an authentication failure.</summary>
    [Fact]
    public async Task A_refusal_changes_no_authentication_or_lockout_state()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 10; i++)
            await SignInAsync(factory, SignerUsername, Password);

        var failedBefore = await SignerEventCountAsync("SignInFailed");
        var lockedBefore = await SignerEventCountAsync("AccountLocked");

        // Wrong passwords, well past the lockout threshold, all refused by the limit.
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await SignInAsync(factory, SignerUsername, "wrong-password-entirely")).Status);
        }

        Assert.Equal((0, false), await SignerCredentialAsync());
        Assert.Equal(failedBefore, await SignerEventCountAsync("SignInFailed"));
        Assert.Equal(lockedBefore, await SignerEventCountAsync("AccountLocked"));
    }

    /// <summary>
    /// RL-12: the behaviour runs before AuthenticationBehavior. Under an
    /// established caller a sign-in is refused with 401 — unless the limit
    /// refused it first.
    /// </summary>
    [Fact]
    public async Task The_limit_is_judged_before_authentication()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        var carrier = await SignInForCarrierAsync(factory);
        var username = Fresh("rl-nobody");

        for (var i = 0; i < 10; i++)
            await SignInAsync(factory, username);

        var refused = await SignInAsync(factory, username, bearer: carrier);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
    }

    /// <summary>RL-7, sign-in.</summary>
    [Fact]
    public async Task Spellings_of_one_username_share_a_bucket()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        var username = Fresh("rl-case");
        var spellings = new[] { username, username.ToUpperInvariant(), $"  {username} " };

        for (var i = 0; i < 10; i++)
            await SignInAsync(factory, spellings[i % spellings.Length]);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SignInAsync(factory, $"\t{username.ToUpperInvariant()}")).Status);
    }

    /// <summary>RL-19: no resolvable address, no IP gate — never one shared bucket.</summary>
    [Fact]
    public async Task Requests_with_no_client_address_are_not_ip_limited()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 31; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(factory, Fresh("rl-anon"))).Status);
    }

    /// <summary>RL-21.</summary>
    [Fact]
    public async Task A_new_host_starts_with_empty_counters()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var username = Fresh("rl-restart");

        await using (var first = new HostFactory())
        {
            for (var i = 0; i < 10; i++)
                await SignInAsync(first, username);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(first, username)).Status);
        }

        await using var second = new HostFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(second, username)).Status);
    }

    // ------------------------------------------------------ forgot password

    /// <summary>RL-3, RL-6: unknown and known addresses alike, and the refusal is byte-identical.</summary>
    [Fact]
    public async Task The_fourth_reset_request_for_one_address_is_refused_whether_or_not_it_exists()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        var unknown = $"{Fresh("rl-unknown")}@example.test";

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await RequestResetAsync(factory, unknown)).Status);

        var unknownRefused = await RequestResetAsync(factory, unknown);

        var requestedBefore = await SignerEventCountAsync("PasswordResetRequested");

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await RequestResetAsync(factory, SignerEmail)).Status);

        var knownRefused = await RequestResetAsync(factory, SignerEmail);

        Assert.Equal(HttpStatusCode.TooManyRequests, unknownRefused.Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, knownRefused.Status);
        Assert.Equal(Refusal, unknownRefused.Body);
        Assert.Equal(unknownRefused.Body, knownRefused.Body);

        // Three requests ran; the fourth issued nothing and recorded nothing.
        Assert.Equal(requestedBefore + 3, await SignerEventCountAsync("PasswordResetRequested"));
        Assert.Equal(
            1,
            await ScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE user_identity_id = @id AND token_type = 'PasswordReset'
                  AND used_at IS NULL AND invalidated_at IS NULL
                """,
                ("id", SignerIdentity)));
    }

    /// <summary>RL-7, forgot password.</summary>
    [Fact]
    public async Task Spellings_of_one_address_share_a_reset_bucket()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        var address = $"{Fresh("rl-case")}@example.test";

        await RequestResetAsync(factory, address.ToUpperInvariant());
        await RequestResetAsync(factory, $" {address} ");
        await RequestResetAsync(factory, address);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestResetAsync(factory, address)).Status);
    }

    /// <summary>RL-3, the IP gate.</summary>
    [Fact]
    public async Task The_eleventh_reset_request_from_one_address_is_refused()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await RequestResetAsync(factory, $"{Fresh("rl-spray")}@example.test", peer: "203.0.113.23")).Status);
        }

        var refused = await RequestResetAsync(factory, $"{Fresh("rl-spray")}@example.test", peer: "203.0.113.23");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
    }

    // --------------------------------------------- reset password and activate

    /// <summary>RL-4: the eleventh is refused even with a valid token, and consumes nothing.</summary>
    [Fact]
    public async Task The_eleventh_reset_from_one_address_is_refused_and_consumes_no_token()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await ResetPasswordAsync(factory, "not-a-token", peer: "203.0.113.24")).Status);
        }

        var (tokenId, plainText) = await IssueSignerTokenAsync(TokenType.PasswordReset);

        var refused = await ResetPasswordAsync(factory, plainText, peer: "203.0.113.24");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
        Assert.False(await TokenUsedAsync(tokenId));
    }

    /// <summary>RL-4.</summary>
    [Fact]
    public async Task The_eleventh_activation_from_one_address_is_refused_and_consumes_no_token()
    {
        await EnsureSignerAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await ActivateAsync(factory, "not-a-token", peer: "203.0.113.25")).Status);
        }

        // The signer is already activated, so this token could never be
        // redeemed in a real system. That does not matter here: a refused
        // request never reaches the handler, so all this proves is that the
        // token is still unused afterwards.
        var (tokenId, plainText) = await IssueSignerTokenAsync(TokenType.Activation);

        var refused = await ActivateAsync(factory, plainText, peer: "203.0.113.25");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal(Refusal, refused.Body);
        Assert.False(await TokenUsedAsync(tokenId));
    }

    /// <summary>Each command's IP bucket is its own.</summary>
    [Fact]
    public async Task One_commands_exhausted_address_does_not_refuse_another_command()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        for (var i = 0; i < 11; i++)
            await ActivateAsync(factory, "not-a-token", peer: "203.0.113.26");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ResetPasswordAsync(factory, "not-a-token", peer: "203.0.113.26")).Status);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SignInAsync(factory, Fresh("rl-other"), peer: "203.0.113.26")).Status);
    }

    // ------------------------------------------------------------- logging

    /// <summary>RL-16: one warning, with the address and the key kind, never the typed username.</summary>
    [Fact]
    public async Task A_refusal_is_logged_once_without_the_typed_username()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var logs = new CapturedLogs();
        await using var factory = new HostFactory(
            services: s => s.AddSingleton<ILoggerProvider>(logs));

        var username = Fresh("rl-secret");

        for (var i = 0; i < 10; i++)
            await SignInAsync(factory, username, peer: "198.51.100.23");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(factory, username, peer: "198.51.100.23")).Status);

        var warning = Assert.Single(
            logs.Entries,
            x => x.Level == LogLevel.Warning && x.Category == typeof(Api.ProblemMiddleware).FullName);

        Assert.Contains("/api/auth/sign-in", warning.Message, StringComparison.Ordinal);
        Assert.Contains("198.51.100.23", warning.Message, StringComparison.Ordinal);
        Assert.Contains("address", warning.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(logs.Entries, x => x.Message.Contains(username, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_refusal_with_no_client_address_logs_the_address_as_unknown()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var logs = new CapturedLogs();
        await using var factory = new HostFactory(
            services: s => s.AddSingleton<ILoggerProvider>(logs));

        var address = $"{Fresh("rl-secret")}@example.test";

        for (var i = 0; i < 4; i++)
            await RequestResetAsync(factory, address);

        var warning = Assert.Single(
            logs.Entries,
            x => x.Level == LogLevel.Warning && x.Category == typeof(Api.ProblemMiddleware).FullName);

        Assert.Contains("/api/account/password-reset-request", warning.Message, StringComparison.Ordinal);
        Assert.Contains("unknown", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, x => x.Message.Contains(address, StringComparison.OrdinalIgnoreCase));
    }
}
