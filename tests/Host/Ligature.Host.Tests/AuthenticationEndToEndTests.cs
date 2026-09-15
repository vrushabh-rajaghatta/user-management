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
    /// So: present a valid carrier to a command that reaches its handler and
    /// fails there — a password change giving the wrong current password. It
    /// answers 400, counts no attempt and records nothing (CRD-C4), and the
    /// activity timestamp must move anyway. If activity were recorded by the
    /// handler rather than by request infrastructure, it would not.
    ///
    /// It used to send a useless token to activation instead. That no longer
    /// reaches a handler: a bearer-authenticated identity-establishing command
    /// may execute only when no caller is already established, so activation
    /// under a live carrier is refused before it starts — which would prove
    /// less than this test claims.
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
                client, "/api/account/change-password", carrier,
                new
                {
                    CurrentPassword = "not-the-current-password",
                    NewPassword = "an-entirely-different-password-3",
                });

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

    // ------------------- bearer commands under an established caller

    /// <summary>
    /// The oracle, closed at the HTTP boundary. A caller signed in as A tries
    /// B's password twice — once right, once wrong. The two answers must be
    /// byte-identical 401s, and B must be untouched: no session, no counted
    /// failure. Before the invariant held, the right password answered 401 and
    /// the wrong one 500, and the difference was the answer to the guess.
    /// </summary>
    [Fact]
    public async Task A_signed_in_caller_cannot_tell_a_correct_password_for_another_account_from_a_wrong_one()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var established = await EnsureEstablishedCallerAsync(client);

            try
            {
                var correct = await SendAsync(
                    client, "/api/auth/sign-in", established,
                    new { Username = actor.Username, Password });

                var wrong = await SendAsync(
                    client, "/api/auth/sign-in", established,
                    new { Username = actor.Username, Password = "not-the-password-at-all" });

                Assert.Equal(
                    (correct.StatusCode, await correct.Content.ReadAsStringAsync()),
                    (wrong.StatusCode, await wrong.Content.ReadAsStringAsync()));

                Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);

                Assert.Null(await TryFindSessionIdAsync(actor.IdentityId));
                Assert.Equal(0, await FailedAttemptsAsync(actor.IdentityId));
            }
            finally
            {
                await EndEstablishedCallerSessionsAsync();
            }
        });
    }

    /// <summary>
    /// Opening someone else's activation link while signed in is refused
    /// before the token is read, so the token is not spent and works as soon
    /// as the request carries no session.
    /// </summary>
    [Fact]
    public async Task A_signed_in_caller_cannot_activate_another_account_and_the_token_survives()
    {
        await RunAsync(async (client, actor) =>
        {
            var established = await EnsureEstablishedCallerAsync(client);

            try
            {
                var refused = await SendAsync(
                    client, "/api/account/activate", established,
                    new { Token = actor.Token, NewPassword = Password });

                Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
                Assert.Equal("Authentication is required.", await ErrorAsync(refused));

                var accepted = await ActivateAsync(client, actor.Token);

                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            }
            finally
            {
                await EndEstablishedCallerSessionsAsync();
            }
        });
    }

    /// <summary>
    /// A second person, distinct from the pending actor, who is permanently
    /// able to sign in. Pinned, because once they have signed in they are the
    /// actor of audit records and cannot be removed.
    /// </summary>
    private static readonly Guid EstablishedCallerUser =
        Guid.Parse("3f6d2a1c-9b84-4e57-a0c3-7d1e5b92f468");

    private static readonly Guid EstablishedCallerIdentity =
        Guid.Parse("b2c7e815-4d3a-4f96-8e21-c5a09d7f3b64");

    private const string EstablishedCallerUsername = "permanent-auth-established-caller";

    /// <summary>Signs the established caller in and returns their carrier.</summary>
    private static async Task<string> EnsureEstablishedCallerAsync(HttpClient client)
    {
        await using (var context = CreateContext())
        {
            if (!await context.Set<User>().AnyAsync(x => x.Id == new UserId(EstablishedCallerUser)))
            {
                var now = DateTimeOffset.UtcNow;

                var user = User.CreateHuman(
                    new UserId(EstablishedCallerUser), "Established", "Caller",
                    "Established Caller", "permanent-auth-established-caller@example.test",
                    now, User.SystemUserId);

                var identity = UserIdentity.CreateLocal(
                    new UserIdentityId(EstablishedCallerIdentity), user.Id, ActorType.Human,
                    EstablishedCallerUsername, now, User.SystemUserId);

                var tokenId = UserTokenId.New();
                var material = new UserTokenService().Generate(tokenId);

                context.AddRange(
                    user,
                    identity,
                    UserToken.Create(
                        tokenId, identity.Id, TokenType.Activation, material.Hash,
                        now, now.AddHours(72), User.SystemUserId));

                await context.SaveChangesAsync(CancellationToken.None);

                (await ActivateAsync(client, material.PlainText)).EnsureSuccessStatusCode();
            }
        }

        return await SignInAsync(client, EstablishedCallerUsername)
            ?? throw new InvalidOperationException("The established caller could not sign in.");
    }

    /// <summary>Sessions are not actors; the person stays, their sessions go.</summary>
    private static async Task EndEstablishedCallerSessionsAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM user_session WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", EstablishedCallerIdentity);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> FailedAttemptsAsync(UserIdentityId identityId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT failed_attempt_count FROM credential WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // ------------------------------------------ credential presentation (B2)

    /// <summary>
    /// The contract cookie name, as a literal rather than CarrierCookie.Name.
    /// </summary>
    private const string CarrierCookieName = "__Host-ligature";

    /// <summary>
    /// The transport added for browsers works through the real pipe: a carrier
    /// presented only as the cookie establishes its session.
    ///
    /// Observed through activity rather than through any endpoint's behaviour.
    /// Both sessions are made stale, so establishment writes last_activity_at
    /// for exactly the session it established and for no other; the probe
    /// request is refused before any command runs, so nothing else can move it.
    /// </summary>
    [Fact]
    public async Task A_carrier_cookie_alone_establishes_its_session_over_http()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.A, sessions.B);

            await ProbeAsync(client, authorization: null, cookie: CarrierCookie(sessions.CarrierB));

            Assert.True(await WasEstablishedAsync(sessions.B, stale));
            Assert.False(await WasEstablishedAsync(sessions.A, stale));
        });
    }

    /// <summary>
    /// The existing transport is unchanged by the new one.
    /// </summary>
    [Fact]
    public async Task A_bearer_carrier_alone_still_establishes_its_session_over_http()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.A, sessions.B);

            await ProbeAsync(client, authorization: $"Bearer {sessions.CarrierA}", cookie: null);

            Assert.True(await WasEstablishedAsync(sessions.A, stale));
            Assert.False(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    [Fact]
    public async Task Over_http_the_authorization_header_wins_over_the_cookie()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.A, sessions.B);

            await ProbeAsync(
                client,
                authorization: $"Bearer {sessions.CarrierA}",
                cookie: CarrierCookie(sessions.CarrierB));

            Assert.True(await WasEstablishedAsync(sessions.A, stale));
            Assert.False(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    /// <summary>
    /// The locked rule, end to end. A forged Bearer header beside a perfectly
    /// valid cookie must not authenticate the request as the cookie's user.
    ///
    /// Proved three ways: the probe establishes nothing, an authenticated
    /// endpoint answers 401, and the cookie's session is neither touched nor
    /// revoked. A 401 alone would not show that the cookie was ignored.
    ///
    /// The final positive control presents the same cookie on its own and
    /// expects it to work, so the negative result cannot be explained by a
    /// cookie that was never presentable.
    /// </summary>
    [Fact]
    public async Task A_forged_bearer_header_never_falls_back_to_a_valid_cookie()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.B);

            var forged = $"Bearer {Forge(sessions.CarrierA)}";
            var cookie = CarrierCookie(sessions.CarrierB);

            await ProbeAsync(client, forged, cookie);

            Assert.False(await WasEstablishedAsync(sessions.B, stale));

            var signOut = await PresentAsync(client, "/api/auth/sign-out", forged, cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, signOut.StatusCode);
            Assert.Equal("Authentication is required.", await ErrorAsync(signOut));

            var row = await ReadSessionAsync(sessions.B);

            Assert.Null(row.RevokedAt);
            Assert.False(await WasEstablishedAsync(sessions.B, stale));

            await ProbeAsync(client, authorization: null, cookie);

            Assert.True(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    /// <summary>
    /// D-B2-2 at the real HTTP layer, where whether an empty header survives is
    /// the server's decision rather than the test harness's.
    /// </summary>
    [Fact]
    public async Task A_blank_authorization_header_lets_the_cookie_establish_over_http()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.B);

            await ProbeAsync(client, authorization: "", cookie: CarrierCookie(sessions.CarrierB));

            Assert.True(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    [Fact]
    public async Task A_carrier_cookie_presented_twice_establishes_nothing_over_http()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.A, sessions.B);

            await ProbeAsync(
                client,
                authorization: null,
                cookie: $"{CarrierCookie(sessions.CarrierA)}; {CarrierCookie(sessions.CarrierB)}");

            Assert.False(await WasEstablishedAsync(sessions.A, stale));
            Assert.False(await WasEstablishedAsync(sessions.B, stale));

            await ProbeAsync(client, authorization: null, CarrierCookie(sessions.CarrierB));

            Assert.True(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    [Fact]
    public async Task A_case_variant_cookie_name_establishes_nothing_over_http()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);
            var stale = await MakeStaleAsync(sessions.B);

            await ProbeAsync(
                client, authorization: null, cookie: $"__host-ligature={sessions.CarrierB}");

            Assert.False(await WasEstablishedAsync(sessions.B, stale));

            await ProbeAsync(client, authorization: null, CarrierCookie(sessions.CarrierB));

            Assert.True(await WasEstablishedAsync(sessions.B, stale));
        });
    }

    /// <summary>
    /// A cookie naming a session that has ended is refused by the platform, and
    /// the pipeline answers exactly as it does for no credential at all.
    ///
    /// Asserted by status, not by activity: RecordActivityAsync never writes a
    /// revoked session, so unchanged activity would pass even if the caller had
    /// been wrongly established. The 401 from an authenticated endpoint is the
    /// evidence here.
    /// </summary>
    [Fact]
    public async Task A_revoked_sessions_carrier_cookie_establishes_nothing()
    {
        await RunAsync(async (client, actor) =>
        {
            var sessions = await TwoSessionsAsync(client, actor);

            // Setup only: end session A through the existing transport.
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await SendAsync(client, "/api/auth/sign-out", sessions.CarrierA)).StatusCode);

            var response = await PresentAsync(
                client, "/api/auth/sign-out",
                authorization: null, cookie: CarrierCookie(sessions.CarrierA));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Authentication is required.", await ErrorAsync(response));
        });
    }

    private sealed record TwoSessions(string CarrierA, Guid A, string CarrierB, Guid B);

    /// <summary>
    /// Two live sessions for the same actor, told apart by the carrier each
    /// sign-in returned rather than by row order.
    /// </summary>
    private static async Task<TwoSessions> TwoSessionsAsync(HttpClient client, Actor actor)
    {
        await ActivateAsync(client, actor.Token);

        var carrierA = await SignInAsync(client, actor.Username)
            ?? throw new InvalidOperationException("The first sign-in failed.");

        var carrierB = await SignInAsync(client, actor.Username)
            ?? throw new InvalidOperationException("The second sign-in failed.");

        return new TwoSessions(carrierA, SessionOf(carrierA), carrierB, SessionOf(carrierB));
    }

    /// <summary>
    /// Five minutes of silence: stale enough that establishment writes activity
    /// (the throttle is 60 seconds), and well inside the 15-minute idle window.
    /// Returns the stale instant so a later write can be recognised.
    /// </summary>
    private static async Task<DateTimeOffset> MakeStaleAsync(params Guid[] sessionIds)
    {
        var now = DateTimeOffset.UtcNow;
        var activity = now.AddMinutes(-5);

        foreach (var sessionId in sessionIds)
        {
            await BackdateAsync(
                sessionId,
                createdAt: now.AddMinutes(-10),
                lastActivityAt: activity,
                expiresAt: now.AddHours(11));
        }

        return activity;
    }

    /// <summary>
    /// True when activity has moved past the stale instant — which happens only
    /// when the per-request check established this session.
    /// </summary>
    private static async Task<bool> WasEstablishedAsync(Guid sessionId, DateTimeOffset stale)
        => (await ReadSessionAsync(sessionId)).LastActivityAt > stale.AddMinutes(1);

    /// <summary>
    /// Sign-in with an empty body is refused as a binding failure before any
    /// command is dispatched, so it has no side effect of its own — but the
    /// request still passes through CallerMiddleware first.
    /// </summary>
    private static async Task ProbeAsync(HttpClient client, string? authorization, string? cookie)
    {
        var response = await PresentAsync(
            client, "/api/auth/sign-in", authorization, cookie, body: new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Headers are added without validation so the test controls exactly what
    /// is sent, including values HttpClient would otherwise reshape.
    /// </summary>
    private static async Task<HttpResponseMessage> PresentAsync(
        HttpClient client,
        string path,
        string? authorization,
        string? cookie,
        object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

        if (cookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", cookie);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private static string CarrierCookie(string carrier) => $"{CarrierCookieName}={carrier}";

    private static Guid SessionOf(string carrier)
        => HostCarrier().Verify(carrier)?.Value
           ?? throw new InvalidOperationException("The carrier did not verify.");

    /// <summary>Changes one character of the signature component only.</summary>
    private static string Forge(string carrier)
    {
        var components = carrier.Split('.');

        components[2] = Tamper(components[2]);

        return string.Join('.', components);
    }

    /// <summary>
    /// An AccessCarrier over the same keys HostFactory configures, so a carrier
    /// the host issued can be verified here to learn which session it names.
    /// </summary>
    private static AccessCarrier HostCarrier()
        => new(
            SigningKeyRing.Load(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>(
                            SigningKeyRing.CurrentKeySetting, HostFactory.PrimaryKeyId),
                        new KeyValuePair<string, string?>(
                            "LIGATURE_SIGNING_KEY_V1", HostFactory.PrimaryKey),
                    ])
                    .Build()));

    // ---------------------------------------- cross-site protection (B3)

    private const string CrossSiteRefusal = "Cross-site requests are not accepted.";

    /// <summary>
    /// A forged sign-in — the one CSRF that SameSite cannot stop, because the
    /// request carries no cookie to withhold — is refused before the command
    /// runs: with credentials that are genuinely valid, no session row exists
    /// afterwards.
    ///
    /// The control sends the same credentials from the same origin and expects
    /// a session, so the refusal cannot be explained by credentials that would
    /// have failed anyway.
    /// </summary>
    [Fact]
    public async Task A_cross_site_sign_in_is_refused_before_any_session_is_created()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var credentials = new { Username = actor.Username, Password };

            var refused = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", credentials, ("Sec-Fetch-Site", "cross-site"));

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(CrossSiteRefusal, await ErrorAsync(refused));
            Assert.Null(await TryFindSessionIdAsync(actor.IdentityId));

            var accepted = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", credentials, ("Sec-Fetch-Site", "same-origin"));

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.NotNull(await TryFindSessionIdAsync(actor.IdentityId));
        });
    }

    /// <summary>
    /// The sibling-subdomain case: SameSite=Strict still sends the cookie on a
    /// same-site request. It is refused, and refused BEFORE CallerMiddleware —
    /// proved by the session's activity, which establishment would have
    /// written. That is the ordering decision, not merely its status code.
    ///
    /// The probe is sign-in with an empty body, so an accepted request answers
    /// 400 from binding and a refused one 403 from this middleware; the control
    /// repeats it from the same origin and expects the activity to move.
    /// </summary>
    [Fact]
    public async Task A_same_site_request_is_refused_before_the_caller_is_established()
    {
        await RunAsync(async (client, actor) =>
        {
            await ActivateAsync(client, actor.Token);

            var carrier = await SignInAsync(client, actor.Username)
                ?? throw new InvalidOperationException("Sign-in failed.");

            var session = SessionOf(carrier);
            var stale = await MakeStaleAsync(session);

            var refused = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", new { },
                ("Cookie", CarrierCookie(carrier)), ("Sec-Fetch-Site", "same-site"));

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(CrossSiteRefusal, await ErrorAsync(refused));
            Assert.False(await WasEstablishedAsync(session, stale));

            var accepted = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", new { },
                ("Cookie", CarrierCookie(carrier)), ("Sec-Fetch-Site", "same-origin"));

            Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
            Assert.True(await WasEstablishedAsync(session, stale));
        });
    }

    /// <summary>
    /// The Origin fallback against the real Request.Host the server derives,
    /// rather than one a unit test chose. The test server is addressed as
    /// localhost.
    /// </summary>
    [Fact]
    public async Task Without_fetch_metadata_the_origin_is_compared_with_the_request_host_over_http()
    {
        await RunAsync(async (client, _) =>
        {
            var ownHost = client.BaseAddress!.Authority;

            var accepted = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", new { }, ("Origin", $"http://{ownHost}"));

            Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);

            var refused = await SendWithHeadersAsync(
                client, "/api/auth/sign-in", new { }, ("Origin", "https://evil.example"));

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(CrossSiteRefusal, await ErrorAsync(refused));
        });
    }

    private static async Task<HttpResponseMessage> SendWithHeadersAsync(
        HttpClient client,
        string path,
        object? body,
        params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Fixed identifiers, because this suite's actor cannot be thrown away any
    /// more. Activating and signing out are both audited, so the user and the
    /// identity are referenced by records no role may delete. Everything that
    /// makes the actor PENDING again — its credential, its history, its
    /// sessions and its tokens — is deleted and recreated per run, and the two
    /// pinned rows are reused rather than multiplied.
    ///
    /// One actor for the whole class: xUnit runs the tests within a class
    /// sequentially, and each run starts from the state the reset establishes
    /// rather than from whatever the previous test left.
    /// </summary>
    private static readonly Guid PendingUser =
        Guid.Parse("6a1d0f74-3c2b-4a51-9e77-0f4c1b8d2a90");

    private static readonly Guid PendingIdentity =
        Guid.Parse("9c84b2e6-57af-4d03-8b19-6e2f7a5c4d11");

    private const string PendingUsername = "permanent-auth-pending";

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
            // The actor's own rows stay. What made this run distinct does not.
            await ResetPendingAsync();
        }
    }

    /// <summary>
    /// The actor, in the pending-activation state: an identity with an
    /// activation token and deliberately NO credential — inv. 15, which exists
    /// as the absence of a row rather than as a status that could drift out of
    /// step with reality.
    ///
    /// Idempotent. The two pinned rows are created once and thereafter only
    /// reset: both statuses go back to Active, because tests here deactivate
    /// them, and a deactivated actor left behind would break the next run.
    /// </summary>
    private static async Task<Actor> SeedPendingAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await ResetPendingAsync();

        await using (var connection = await TestDatabase.OpenAsync())
        {
            await using (var user = new NpgsqlCommand(
                """
                INSERT INTO app_user
                    (id, actor_type, first_name, last_name, display_name, email,
                     status, created_at, created_by, updated_at, updated_by)
                VALUES
                    (@id, 'Human', 'Host', 'Tester', 'Host Tester',
                     'permanent-auth-pending@example.test',
                     'Active', @now, @system, @now, @system)
                ON CONFLICT (id) DO UPDATE SET status = 'Active'
                """, connection))
            {
                user.Parameters.AddWithValue("id", PendingUser);
                user.Parameters.AddWithValue("now", now);
                user.Parameters.AddWithValue("system", User.SystemUserId.Value);

                await user.ExecuteNonQueryAsync();
            }

            await using var identity = new NpgsqlCommand(
                """
                INSERT INTO user_identity
                    (id, user_id, actor_type, identity_type, identity_provider,
                     subject_id, username, status, created_at, created_by)
                VALUES
                    (@id, @user, 'Human', 'Local', 'Application',
                     @username, @username, 'Active', @now, @system)
                ON CONFLICT (id) DO UPDATE SET status = 'Active'
                """, connection);

            identity.Parameters.AddWithValue("id", PendingIdentity);
            identity.Parameters.AddWithValue("user", PendingUser);
            identity.Parameters.AddWithValue("username", PendingUsername);
            identity.Parameters.AddWithValue("now", now);
            identity.Parameters.AddWithValue("system", User.SystemUserId.Value);

            await identity.ExecuteNonQueryAsync();
        }

        // The real token service, so the stored hash is the one the real
        // consumption path will recompute.
        var tokenService = new UserTokenService();
        var tokenId = UserTokenId.New();
        var material = tokenService.Generate(tokenId);

        await using (var context = CreateContext())
        {
            context.Add(UserToken.Create(
                tokenId, new UserIdentityId(PendingIdentity), TokenType.Activation,
                material.Hash, now, now.AddHours(72), User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        return new Actor(
            new UserId(PendingUser), new UserIdentityId(PendingIdentity),
            PendingUsername, material.PlainText);
    }

    /// <summary>
    /// Everything the actor accumulated, without touching the actor. These are
    /// the rows the trail does not point at, so they are the rows that may go.
    /// </summary>
    private static async Task ResetPendingAsync()
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
            command.Parameters.AddWithValue("id", PendingIdentity);
            await command.ExecuteNonQueryAsync();
        }
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

    /// <summary>
    /// Re-creates the session with the timestamps it would have had if it had
    /// been established that long ago.
    ///
    /// It used to backdate with an UPDATE. G4 made created_at and expires_at
    /// immutable, so a session's age is now fixed at insert — which is the
    /// point: nothing may silently extend a session past the absolute timeout
    /// the effective policy set, a test fixture included.
    ///
    /// One statement. The DELETE's RETURNING feeds the INSERT, so every other
    /// column travels across unchanged and the row keeps its id — the carrier
    /// the caller is holding still names this session.
    /// </summary>
    private static async Task BackdateAsync(
        Guid sessionId,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        DateTimeOffset expiresAt)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            WITH removed AS (
                DELETE FROM user_session WHERE id = @id RETURNING *
            )
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at,
                 revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
            SELECT id, user_identity_id, @created, @activity, @expires,
                   revoked_at, revoked_by, revocation_reason, ip_address, user_agent
            FROM removed
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
