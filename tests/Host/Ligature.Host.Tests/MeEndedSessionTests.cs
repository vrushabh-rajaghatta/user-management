using System.Net;
using System.Net.Http.Headers;
using Ligature.Host.Authentication;
using Ligature.Platform.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// GET /api/me for a carrier whose SIGNATURE is valid but whose session the
/// authentication boundary refuses.
///
/// CallerMiddleware records the SessionId of every carrier that verifies, and
/// separately asks the establisher whether that session yields a caller. A
/// valid signature therefore says nothing about whether the session is live,
/// and /me used to take the recorded SessionId as though it did: every state
/// below answered 500, where the client's contract needs 401 ("no caller")
/// rather than 5xx ("we do not know"). After an idle timeout, that left a user
/// on an error screen instead of the sign-in page.
///
/// Every refused state must be indistinguishable from presenting no carrier at
/// all: the same status and the same body, with nothing about the session.
///
/// THE CONTROL: a live session, minted the same way, answers 200. Without it a
/// broken minting step would make every 401 here pass for the wrong reason.
///
/// Carriers are minted with the host's own AccessCarrier, so they are signed
/// exactly as sign-in signs them. The users here never act, so nothing
/// prevents removing them.
/// </summary>
public sealed class MeEndedSessionTests
{
    public enum State
    {
        Missing,
        Revoked,
        AbsolutelyExpired,
        IdleExpired,
        InactiveIdentity,
        InactiveUser,
    }

    [Fact]
    public async Task A_live_session_minted_the_same_way_is_described()
    {
        await RunAsync(State.Missing, live: true, async (client, carrier, _) =>
        {
            var response = await MeAsync(client, carrier);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    [Theory]
    [InlineData(State.Missing)]
    [InlineData(State.Revoked)]
    [InlineData(State.AbsolutelyExpired)]
    [InlineData(State.IdleExpired)]
    [InlineData(State.InactiveIdentity)]
    [InlineData(State.InactiveUser)]
    public async Task A_session_the_boundary_refuses_is_answered_exactly_as_no_carrier_is(State state)
    {
        await RunAsync(state, live: false, async (client, carrier, anonymous) =>
        {
            var response = await MeAsync(client, carrier);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(anonymous, await response.Content.ReadAsStringAsync());
        });
    }

    // ------------------------------------------------------------ harness

    private static async Task RunAsync(State state, bool live, Func<HttpClient, string, string, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory();

        var client = factory.CreateClient();

        var anonymousResponse = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var anonymous = await anonymousResponse.Content.ReadAsStringAsync();

        var (userId, sessionId) = await SeedAsync(live ? null : state);

        try
        {
            var carrier = factory.Services.GetRequiredService<AccessCarrier>().Issue(new UserSessionId(sessionId));

            await body(client, carrier, anonymous);
        }
        finally
        {
            await DeleteAsync(userId);
        }
    }

    private static async Task<HttpResponseMessage> MeAsync(HttpClient client, string carrier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", carrier);

        return await client.SendAsync(request);
    }

    /// <summary>
    /// A human with one local identity and one session, in the given state, or
    /// live when <paramref name="state"/> is null. For Missing, the session row
    /// is not written at all: its id is minted but refers to nothing.
    /// </summary>
    private static async Task<(Guid UserId, Guid SessionId)> SeedAsync(State? state)
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var unique = userId.ToString("N");

        var userActive = state != State.InactiveUser;
        var identityActive = state != State.InactiveIdentity;

        // Created two hours ago. Live: active a moment ago, eleven hours left.
        var lastActivity = state == State.IdleExpired ? "now() - interval '90 minutes'" : "now() - interval '1 minute'";
        var expires = state == State.AbsolutelyExpired ? "now() - interval '5 minutes'" : "now() + interval '10 hours'";
        var revoked = state == State.Revoked
            ? $"now() - interval '1 minute', '{system}', 'Revoked for the /me ended-session test.'"
            : "NULL, NULL, NULL";

        await using var connection = await TestDatabase.OpenAsync();

        var sql = $"""
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 deactivated_at, deactivated_by,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                ('{userId}', 'Human', 'Ended', 'Session', 'Ended Session {unique[..8]}',
                 'ended-{unique}@example.test', '{(userActive ? "Active" : "Inactive")}',
                 {(userActive ? "NULL, NULL" : $"now(), '{system}'")},
                 now() - interval '1 day', '{system}', now(), '{system}');

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, deactivated_at, deactivated_by,
                 created_at, created_by)
            VALUES
                ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                 '{identityId}', 'ended-{unique}', '{(identityActive ? "Active" : "Inactive")}',
                 {(identityActive ? "NULL, NULL" : $"now(), '{system}'")},
                 now() - interval '1 day', '{system}');
            """;

        if (state != State.Missing)
        {
            sql += $"""

                INSERT INTO user_session
                    (id, user_identity_id, created_at, last_activity_at, expires_at,
                     revoked_at, revoked_by, revocation_reason)
                VALUES
                    ('{sessionId}', '{identityId}', now() - interval '2 hours', {lastActivity}, {expires},
                     {revoked});
                """;
        }

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();

        return (userId, sessionId);
    }

    private static async Task DeleteAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_session WHERE user_identity_id IN (SELECT id FROM user_identity WHERE user_id = @id)",
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
