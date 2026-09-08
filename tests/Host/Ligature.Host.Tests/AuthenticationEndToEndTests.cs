using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// The whole boundary, over HTTP, against real PostgreSQL:
///
/// <code>
/// HTTP -> middleware -> carrier verification -> caller establishment
///      -> endpoint    -> command pipeline    -> persistence -> PostgreSQL
/// </code>
///
/// Nothing here is stubbed, because the seam between those layers is the only
/// thing this project adds and testing the endpoint methods in isolation would
/// prove none of it.
///
/// Provisioning is EXPLICIT test setup. There is no startup provisioning and no
/// CLI verb — deliberately, and out of scope here — so these tests seed the
/// rows they need directly and delete them afterwards.
///
/// The real SystemClock runs, so these use real instants and manipulate stored
/// timestamps when they need a session to be old.
/// </summary>
public sealed class AuthenticationEndToEndTests
{
    /// <summary>
    /// Comfortably over the baseline PasswordMinLength of 12, which is a floor
    /// the effective policy can raise but never lower.
    /// </summary>
    private const string Password = "correct-horse-battery-staple";

    // -------------------------------------------------------- the journey

    /// <summary>
    /// The story this slice exists to tell: a pending account activates with an
    /// emailed token, signs in and receives a carrier, uses it to sign out, and
    /// the session row ends up revoked as a self-revocation.
    /// </summary>
    [Fact]
    public async Task A_pending_account_activates_signs_in_and_signs_out()
    {
        await RunAsync(async (client, actor) =>
        {
            var activation = await ActivateAsync(client, actor.Token);

            Assert.Equal(HttpStatusCode.OK, activation.StatusCode);

            var carrier = await SignInAsync(client, actor.Username);

            Assert.NotNull(carrier);

            // Section 17's shape, arriving over the wire rather than from a
            // unit test's helper.
            Assert.Equal(3, carrier!.Split('.').Length);

            var sessionId = await SessionIdAsync(actor.IdentityId);

            Assert.Null((await ReadSessionAsync(sessionId)).RevokedAt);

            var signOut = await SendAsync(client, "/api/auth/sign-out", carrier);

            Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

            var session = await ReadSessionAsync(sessionId);

            // D6 — one termination mechanism. Signing out writes the same three
            // columns an administrator or a cascade would.
            Assert.NotNull(session.RevokedAt);
            Assert.Equal(actor.UserId.Value, session.RevokedBy);
            Assert.Equal("Logout", session.RevocationReason);
        });
    }

    // ------------------------------------------------ absent and forged

    /// <summary>
    /// An absent Authorization header is NOT an authentication failure. The
    /// middleware establishes no caller and the pipeline decides — and for an
    /// anonymous command the answer is "run it".
    ///
    /// The 400 here is the activation token being rubbish, which is the proof:
    /// the request reached the handler.
    /// </summary>
    [Fact]
    public async Task An_absent_header_still_reaches_an_anonymous_endpoint()
    {
        await RunAsync(async (client, _) =>
        {
            var response = await ActivateAsync(client, "not-a-real-token");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            Assert.Equal(
                "The activation token is not valid.",
                await ErrorAsync(response));
        });
    }

    /// <summary>
    /// The same absent header on an AUTHENTICATED command is refused — by the
    /// pipeline, not by the middleware.
    /// </summary>
    [Fact]
    public async Task An_authenticated_endpoint_refuses_an_absent_carrier()
    {
        await RunAsync(async (client, _) =>
        {
            var response = await SendAsync(client, "/api/auth/sign-out", carrier: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    /// <summary>
    /// THE indistinguishability test. A forged carrier, a syntactically absurd
    /// one and no carrier at all must produce byte-identical responses —
    /// otherwise a caller can tell which of their guesses was closer.
    /// </summary>
    [Fact]
    public async Task Every_kind_of_invalid_carrier_answers_identically()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var valid = await SignInAsync(client, actor.Username);

            var components = valid!.Split('.');

            var forged = $"{components[0]}.{components[1]}.{Tamper(components[2])}";

            // A real session id, signed with nothing anyone would accept.
            var unknownKey = $"v9.{components[1]}.{components[2]}";

            var responses = new List<(HttpStatusCode Status, string Body)>();

            foreach (var candidate in new[]
                     { null, "garbage", forged, unknownKey, "v1..", })
            {
                var response = await SendAsync(
                    client, "/api/auth/sign-out", candidate);

                responses.Add(
                    (response.StatusCode,
                     await response.Content.ReadAsStringAsync()));
            }

            Assert.All(
                responses,
                x => Assert.Equal(responses[0], x));

            Assert.Equal(HttpStatusCode.Unauthorized, responses[0].Status);

            // And the body is the FIXED sentence rather than whatever the
            // pipeline's exception happened to say. Passing the exception
            // message through would make a future, more specific
            // AuthenticationFailedException leak the moment someone wrote one.
            Assert.Equal(
                """{"error":"Authentication is required."}""",
                responses[0].Body);

            // And nothing was revoked by any of them.
            Assert.Null(
                (await ReadSessionAsync(
                    await SessionIdAsync(actor.IdentityId))).RevokedAt);
        });
    }

    /// <summary>
    /// Section 17's pass-through, and the property that makes anonymous
    /// endpoints reachable at all: an INVALID carrier must be treated exactly
    /// like an absent one. The middleware establishes no caller and the
    /// pipeline decides — and for an anonymous command the answer is still
    /// "run it".
    ///
    /// Without this, a client whose session quietly expired could no longer
    /// activate an account or sign in, because their stale carrier would be
    /// refused before the handler was ever reached.
    /// </summary>
    [Fact]
    public async Task An_invalid_carrier_still_reaches_an_anonymous_endpoint()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);

            // Revoke it, so the carrier is now valid in shape but names a dead
            // session — the middleware will fail to establish a caller.
            await SendAsync(client, "/api/auth/sign-out", carrier);

            var response = await SendAsync(
                client, "/api/account/activate", carrier,
                new { Token = "not-a-real-token", NewPassword = Password });

            // 400 from the handler, NOT 401 from the middleware. The status is
            // the whole assertion: it says the request got through.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            Assert.Equal(
                "The activation token is not valid.",
                await ErrorAsync(response));
        });
    }

    /// <summary>
    /// Inv. 26 — sessions exist for interactive HUMAN authentication, so a
    /// session held by anything else must not establish a caller.
    ///
    /// Unreachable through the front door: AU11 blocks agent creation and
    /// SES-C1 refuses to create a session for a non-human identity. So the rows
    /// are seeded directly, which is the only way to prove the check does
    /// something rather than merely being unreachable.
    /// </summary>
    [Fact]
    public async Task A_session_held_by_a_non_human_actor_establishes_nothing()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var agent = await SeedAgentSessionAsync();

        try
        {
            await using var factory = new HostFactory();

            var client = factory.CreateClient();

            // Signed with the same key the host under test is configured
            // with, so this carrier is genuinely valid — the rejection has to
            // come from the session check, not from the signature.
            var carrier = CarrierFor(agent.SessionId);

            var response = await SendAsync(client, "/api/auth/sign-out", carrier);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // And nothing was revoked, because no caller was ever established.
            Assert.Null((await ReadSessionAsync(agent.SessionId)).RevokedAt);
        }
        finally
        {
            await DeleteActorAsync(agent.UserId);
        }
    }

    /// <summary>
    /// Two DIFFERENT code paths produce these two 401s, and they must be
    /// indistinguishable.
    ///
    /// An unverifiable carrier never yields a SessionId, so the endpoint
    /// refuses it before dispatching anything. A carrier naming a REVOKED
    /// session yields one, gets dispatched, and is refused by
    /// AuthenticationBehavior inside the pipeline — a different layer, a
    /// different exception, a different response writer.
    ///
    /// If those two answered differently, a caller could tell "this carrier is
    /// forged" from "this session has been revoked", which is exactly the
    /// distinction section 17 collapses.
    /// </summary>
    [Fact]
    public async Task A_forged_carrier_and_a_revoked_one_answer_identically()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);

            // Refused by the endpoint: nothing verifies, so there is no
            // SessionId to dispatch with.
            var forged = await SendAsync(client, "/api/auth/sign-out", "garbage");

            // Refused by the pipeline: this one verifies and dispatches, and
            // AuthenticationBehavior rejects it because establishment failed.
            await SendAsync(client, "/api/auth/sign-out", carrier);
            var revoked = await SendAsync(client, "/api/auth/sign-out", carrier);

            Assert.Equal(forged.StatusCode, revoked.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

            Assert.Equal(
                await forged.Content.ReadAsStringAsync(),
                await revoked.Content.ReadAsStringAsync());

            Assert.Equal(
                """{"error":"Authentication is required."}""",
                await revoked.Content.ReadAsStringAsync());
        });
    }

    // ------------------------------------------------- session validity

    /// <summary>
    /// The carrier has no independent lifetime, so a revoked session must
    /// disable it immediately. If the carrier carried its own expiry this would
    /// keep working until that expiry — which is exactly why it does not.
    /// </summary>
    [Fact]
    public async Task A_revoked_sessions_carrier_stops_working()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);

            // Same carrier, now naming a revoked session.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    [Fact]
    public async Task An_expired_sessions_carrier_is_refused()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;

            // ck_user_session_expires_at requires expiry after creation, so an
            // expired session is one created earlier still.
            await BackdateAsync(
                sessionId,
                createdAt: now.AddHours(-13),
                lastActivityAt: now.AddHours(-13),
                expiresAt: now.AddHours(-1));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    /// <summary>
    /// Baseline SessionIdleTimeout is 15 minutes and the enforcement tolerance
    /// is 60 seconds, so twenty minutes of silence is idle by any reading.
    /// </summary>
    [Fact]
    public async Task An_idle_sessions_carrier_is_refused()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;

            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-30),
                lastActivityAt: now.AddMinutes(-20),
                expiresAt: now.AddHours(11));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    /// <summary>
    /// US7 — the effective idle timeout may EXCEED the configured value by the
    /// documented tolerance but must never fall short. A request 15 minutes and
    /// 30 seconds after the last recorded activity is inside the 60-second
    /// tolerance and must still be accepted, because the stored value is
    /// throttled and may lag reality.
    ///
    /// Reduce the tolerance to zero and this test fails, which is the point:
    /// without it a genuinely active user is signed out early.
    /// </summary>
    [Fact]
    public async Task A_session_just_inside_the_tolerance_is_still_accepted()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;

            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-30),
                lastActivityAt: now.AddMinutes(-15).AddSeconds(-30),
                expiresAt: now.AddHours(11));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    /// <summary>
    /// The reason the check is per-request rather than baked into a token: a
    /// deactivation takes effect on the very next request. With a
    /// self-contained token it would take effect whenever the token lapsed,
    /// which D7 rejected as not honest.
    /// </summary>
    [Fact]
    public async Task A_deactivated_user_is_refused_on_the_next_request()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);

            await DeactivateUserAsync(actor.UserId);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    /// <summary>
    /// The identity, not the user. Both must be active, and they deactivate
    /// independently — a user may keep their account while one of their
    /// identities is withdrawn. A check that only looked at the user would
    /// leave a withdrawn identity's sessions alive.
    /// </summary>
    [Fact]
    public async Task A_deactivated_identity_is_refused_on_the_next_request()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);

            await DeactivateIdentityAsync(actor.IdentityId);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    /// <summary>
    /// The catch-all path, and the one that must never say anything. A body
    /// that is not JSON at all fails inside the framework's model binding
    /// rather than in any handler, which is exactly the class of failure whose
    /// message would otherwise carry parser, schema or SQL detail to a caller.
    ///
    /// Asserted as an ALLOWLIST would be: the response may contain the one
    /// fixed sentence and nothing else.
    /// </summary>
    [Fact]
    public async Task An_unexpected_failure_reports_no_detail()
    {
        await RunAsync(async (client, _) =>
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/auth/sign-in")
            {
                Content = new StringContent(
                    "{ this is not json at all",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };

            var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.InternalServerError,
                $"Unexpected status {response.StatusCode}.");

            var body = await response.Content.ReadAsStringAsync();

            // No exception type, no stack, no parser detail, no path.
            foreach (var leak in new[]
                     {
                         "Exception", "System.", "at Ligature",
                         "JSON", "json", "line", "Npgsql", "Microsoft",
                     })
            {
                Assert.DoesNotContain(leak, body, StringComparison.Ordinal);
            }
        });
    }

    // ------------------------------------------------- session activity

    /// <summary>
    /// Section 17: activity represents the authenticated REQUEST, not the
    /// successful completion of business work — "a failed command is still
    /// legitimate activity".
    ///
    /// So: present a valid carrier to an ANONYMOUS endpoint and send it a token
    /// that cannot possibly work. The command fails with a 400 and the activity
    /// timestamp must move anyway. If activity were recorded by the handler
    /// rather than by request infrastructure, it would not.
    /// </summary>
    [Fact]
    public async Task A_failed_command_still_records_activity()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;

            // Staler than the 60-second throttle, so a write is due.
            var stale = now.AddMinutes(-5);

            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-10),
                lastActivityAt: stale,
                expiresAt: now.AddHours(11));

            var response = await SendAsync(
                client, "/api/account/activate", carrier,
                new { Token = "still-not-a-token", NewPassword = Password });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var recorded = (await ReadSessionAsync(sessionId)).LastActivityAt;

            Assert.True(
                recorded > stale,
                $"Activity should have moved from {stale:O} but is {recorded:O}. "
                + "A failed command is still legitimate activity.");
        });
    }

    /// <summary>
    /// And the throttle: a second request moments later must NOT write again.
    /// Without it every authenticated request is a write, which is the whole
    /// reason the enforcement tolerance exists.
    /// </summary>
    [Fact]
    public async Task Activity_is_not_rewritten_within_the_throttle_window()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;

            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-10),
                lastActivityAt: now.AddMinutes(-5),
                expiresAt: now.AddHours(11));

            // First request writes.
            await SendAsync(
                client, "/api/account/activate", carrier,
                new { Token = "nope", NewPassword = Password });

            var first = (await ReadSessionAsync(sessionId)).LastActivityAt;

            // Second request, well inside 60 seconds of the first, must not.
            await SendAsync(
                client, "/api/account/activate", carrier,
                new { Token = "nope", NewPassword = Password });

            Assert.Equal(first, (await ReadSessionAsync(sessionId)).LastActivityAt);
        });
    }

    /// <summary>
    /// Validity is evaluated BEFORE activity is recorded. Reverse the two and
    /// an already-idle session resurrects itself by making one more request —
    /// the idle check reads last_activity_at, so writing it first would always
    /// satisfy the check that was meant to reject.
    /// </summary>
    [Fact]
    public async Task An_idle_session_does_not_resurrect_itself()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username);
            var sessionId = await SessionIdAsync(actor.IdentityId);

            var now = DateTimeOffset.UtcNow;
            var idle = now.AddMinutes(-20);

            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-30),
                lastActivityAt: idle,
                expiresAt: now.AddHours(11));

            await SendAsync(client, "/api/auth/sign-out", carrier);

            // Untouched. Had activity been recorded first, the session would
            // now look fresh and the NEXT request would succeed.
            Assert.Equal(
                idle.ToUnixTimeSeconds(),
                (await ReadSessionAsync(sessionId)).LastActivityAt.ToUnixTimeSeconds());

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SendAsync(client, "/api/auth/sign-out", carrier)).StatusCode);
        });
    }

    // --------------------------------------------------------- sign-in

    /// <summary>
    /// A wrong password and an unknown username are the same 401 with the same
    /// body. SignInResult carries no reason, and the endpoint must not invent
    /// one.
    /// </summary>
    [Fact]
    public async Task A_failed_sign_in_reveals_nothing_and_issues_nothing()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var wrongPassword = await client.PostAsJsonAsync(
                "/api/auth/sign-in",
                new { actor.Username, Password = "wrong-password-entirely" });

            var unknownUser = await client.PostAsJsonAsync(
                "/api/auth/sign-in",
                new { Username = "nobody-at-all", Password });

            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

            Assert.Equal(
                await wrongPassword.Content.ReadAsStringAsync(),
                await unknownUser.Content.ReadAsStringAsync());

            // And no session was created for the failed attempt.
            Assert.Null(await TryFindSessionIdAsync(actor.IdentityId));
        });
    }

    /// <summary>
    /// A password below the effective floor is rejected AND the activation
    /// token survives — burning it on a rejected password would leave the user
    /// permanently unable to activate. Proved by activating properly afterwards
    /// with the same token.
    /// </summary>
    [Fact]
    public async Task A_rejected_password_does_not_burn_the_activation_token()
    {
        await RunAsync(async (client, actor) =>
        {
            var tooShort = await SendAsync(
                client, "/api/account/activate", carrier: null,
                new { actor.Token, NewPassword = "short" });

            Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

            Assert.Equal(
                HttpStatusCode.OK,
                (await ActivateAsync(client, actor.Token)).StatusCode);
        });
    }

    // ------------------------------------------------------------ harness

    private sealed record Actor(
        UserId UserId,
        UserIdentityId IdentityId,
        string Username,
        string Token);

    private static async Task RunAsync(Func<HttpClient, Actor, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var actor = await SeedPendingAsync();

        try
        {
            await using var factory = new HostFactory();

            await body(factory.CreateClient(), actor);
        }
        finally
        {
            await DeleteActorAsync(actor.UserId);
        }
    }

    /// <summary>
    /// A user with an identity and an activation token, and deliberately NO
    /// credential — inv. 15, the pending-activation state, which exists as the
    /// absence of a row rather than as a status that could drift out of step
    /// with reality.
    /// </summary>
    private static async Task<Actor> SeedPendingAsync()
    {
        var discriminator = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            UserId.New(), "Host", "Tester",
            $"Host Tester {discriminator[..8]}",
            $"host-{discriminator}@example.test", now, User.SystemUserId);

        var username = $"host-{discriminator[..12]}";

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(), user.Id, ActorType.Human,
            username, now, User.SystemUserId);

        // The real token service, so the stored hash is the one the real
        // consumption path will recompute.
        var tokenService = new UserTokenService();
        var tokenId = UserTokenId.New();
        var material = tokenService.Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash,
            now, now.AddHours(72), User.SystemUserId);

        await using var context = CreateContext();

        context.AddRange(user, identity, token);

        await context.SaveChangesAsync(CancellationToken.None);

        return new Actor(user.Id, identity.Id, username, material.PlainText);
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

    private static Task<HttpResponseMessage> ActivateAsync(
        HttpClient client, string token)
        => client.PostAsJsonAsync(
            "/api/account/activate",
            new { Token = token, NewPassword = Password });

    private static async Task<string?> SignInAsync(
        HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in", new { Username = username, Password });

        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("accessToken").GetString();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string path, string? carrier, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (carrier is not null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", carrier);
        }

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("error").GetString();
    }

    private static string Tamper(string signature)
    {
        var characters = signature.ToCharArray();

        characters[0] = characters[0] == 'A' ? 'B' : 'A';

        return new string(characters);
    }

    // ------------------------------------------------------- the database

    private sealed record SessionRow(
        DateTimeOffset LastActivityAt,
        DateTimeOffset? RevokedAt,
        Guid? RevokedBy,
        string? RevocationReason);

    private static async Task<Guid> SessionIdAsync(UserIdentityId identityId)
        => await TryFindSessionIdAsync(identityId)
           ?? throw new InvalidOperationException(
               "No session row exists for this identity.");

    private static async Task<Guid?> TryFindSessionIdAsync(
        UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT id FROM user_session WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return await command.ExecuteScalarAsync() as Guid?;
    }

    private static async Task<SessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT last_activity_at, revoked_at, revoked_by, revocation_reason
            FROM user_session WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The session row is missing.");

        return new SessionRow(
            Offset(reader.GetValue(0))!.Value,
            reader.IsDBNull(1) ? null : Offset(reader.GetValue(1)),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    /// <summary>
    /// Npgsql hands back DateTime for timestamptz through the untyped
    /// GetValue, so an unguarded cast to DateTimeOffset throws.
    /// </summary>
    private static DateTimeOffset? Offset(object? value)
        => value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };

    private static async Task BackdateAsync(
        Guid sessionId,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        DateTimeOffset expiresAt)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            UPDATE user_session
            SET created_at = @created,
                last_activity_at = @activity,
                expires_at = @expires
            WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("created", createdAt);
        command.Parameters.AddWithValue("activity", lastActivityAt);
        command.Parameters.AddWithValue("expires", expiresAt);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// ck_app_user_deactivation_pair requires both stamp columns together.
    /// </summary>
    private static async Task DeactivateUserAsync(UserId userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            UPDATE app_user
            SET status = 'Inactive',
                deactivated_at = @at,
                deactivated_by = @by
            WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", userId.Value);
        command.Parameters.AddWithValue("at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("by", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mints a carrier outside the host for a session the host could never have
    /// issued one for.
    /// </summary>
    private static string CarrierFor(Guid sessionId)
        => new AccessCarrier(
            SigningKeyRing.Load(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>(
                            SigningKeyRing.CurrentKeySetting,
                            HostFactory.PrimaryKeyId),
                        new KeyValuePair<string, string?>(
                            "LIGATURE_SIGNING_KEY_V1", HostFactory.PrimaryKey),
                    ])
                    .Build()))
            .Issue(new UserSessionId(sessionId));

    private sealed record AgentActor(UserId UserId, Guid SessionId);

    /// <summary>
    /// Raw SQL on purpose. User.CreateHuman is the only public factory, SES-C1
    /// refuses a non-human session, and AU11 blocks agent creation — so the
    /// domain offers no legitimate route to this state, which is precisely why
    /// the check being tested must not depend on one existing.
    /// </summary>
    private static async Task<AgentActor> SeedAgentSessionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var discriminator = Guid.NewGuid().ToString("N")[..12];

        await using var connection = await TestDatabase.OpenAsync();

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, display_name, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES (@id, 'Agent', @name, 'Active', @now, @system, @now, @system)
            """, connection))
        {
            command.Parameters.AddWithValue("id", userId);
            command.Parameters.AddWithValue("name", $"Agent {discriminator}");
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES (@id, @user, 'Agent', 'Local', 'Local',
                    @subject, @subject, 'Active', @now, @system)
            """, connection))
        {
            command.Parameters.AddWithValue("id", identityId);
            command.Parameters.AddWithValue("user", userId);
            command.Parameters.AddWithValue("subject", $"agent-{discriminator}");
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at)
            VALUES (@id, @identity, @now, @now, @expires)
            """, connection))
        {
            command.Parameters.AddWithValue("id", sessionId);
            command.Parameters.AddWithValue("identity", identityId);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("expires", now.AddHours(12));

            await command.ExecuteNonQueryAsync();
        }

        return new AgentActor(new UserId(userId), sessionId);
    }

    private static async Task DeactivateIdentityAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            UPDATE user_identity
            SET status = 'Inactive',
                deactivated_at = @at,
                deactivated_by = @by
            WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);
        command.Parameters.AddWithValue("at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("by", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteActorAsync(UserId userId)
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
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }
}
