using SKSMCorp.Platform.Application.Users.Commands.RevokeUserSessions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;
using Kind = SKSMCorp.Platform.Persistence.Tests.SessionRevocationSeed.Kind;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// SES-C4, administrator form, end to end. User-wide (D5): the selection spans
/// every identity of the user and stops exactly at the canonical active-session
/// test and at the user's boundary.
/// </summary>
public sealed class RevokeUserSessionsIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Explanation = "SUP-9002: laptop reported stolen.";

    private readonly SessionRevocationSeed _seed;

    public RevokeUserSessionsIntegrationTests(ActivationDatabase database)
        => _seed = new SessionRevocationSeed(database);

    [Fact]
    public async Task Every_active_session_of_every_identity_is_revoked_and_nothing_else()
    {
        var admin = await _seed.CallerAsync("ses-c4-administrator", "user-administrator");
        var holder = await _seed.PersonAsync();
        var external = await _seed.IdentityAsync(holder.UserId, external: true);

        var localActive = await _seed.SessionAsync(holder.LocalIdentityId);
        var localPastTolerance = await _seed.SessionAsync(holder.LocalIdentityId, Kind.IdlePastTolerance);
        var externalActive = await _seed.SessionAsync(external);
        var externalWithinTolerance = await _seed.SessionAsync(external, Kind.IdleWithinTolerance);
        var alreadyRevoked = await _seed.SessionAsync(holder.LocalIdentityId, Kind.Revoked);
        var expiredButRecent = await _seed.SessionAsync(external, Kind.ExpiredRecentlyActive);

        var stranger = await _seed.PersonAsync();
        var strangersSession = await _seed.SessionAsync(stranger.LocalIdentityId);

        var untouched = new[] { localPastTolerance, alreadyRevoked, expiredButRecent, strangersSession };
        var before = await _seed.FootprintAsync(untouched);

        await RevokeAsync(admin.UserId, holder.UserId);

        foreach (var (session, identity) in new[]
                 {
                     (localActive, holder.LocalIdentityId),
                     (externalActive, external),
                     (externalWithinTolerance, external),
                 })
        {
            var row = await _seed.ReadSessionAsync(session);

            Assert.True(row.IsRevoked);
            Assert.Equal(admin.UserId.Value, row.RevokedBy);
            Assert.Equal("AdminRevoked", row.Reason);

            var record = Assert.Single(await _seed.ReadRevocationsAsync(session));

            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(admin.UserId.Value, record.ActorUserId);
            Assert.Equal(Explanation, record.Reason);
            Assert.Equal("AdminRevoked", record.Code);
            Assert.Equal(identity, record.TargetIdentity);
            Assert.Equal(holder.UserId.Value, record.SubjectUser);
        }

        Assert.Equal(before, await _seed.FootprintAsync(untouched));
    }

    /// <summary>D4 — no exclusion: an administrator targeting themselves ends their own current session.</summary>
    [Fact]
    public async Task An_administrator_targeting_themselves_ends_their_own_current_session()
    {
        var admin = await _seed.CallerAsync("ses-c4-self-administrator", "user-administrator");
        var current = await _seed.SessionAsync(admin.IdentityId.Value);

        await RevokeAsync(admin.UserId, admin.UserId);

        Assert.True((await _seed.ReadSessionAsync(current)).IsRevoked);
    }

    [Fact]
    public async Task A_user_with_no_active_sessions_is_a_no_op()
    {
        var admin = await _seed.CallerAsync("ses-c4-administrator", "user-administrator");
        var holder = await _seed.PersonAsync();
        var ended = new[]
        {
            await _seed.SessionAsync(holder.LocalIdentityId, Kind.Expired),
            await _seed.SessionAsync(holder.LocalIdentityId, Kind.Revoked),
        };

        var before = await _seed.FootprintAsync(ended);

        await RevokeAsync(admin.UserId, holder.UserId);

        Assert.Equal(before, await _seed.FootprintAsync(ended));
    }

    /// <summary>
    /// The canonical test includes the actor type: the per-request check refuses
    /// a non-human session, so it is not active and there is nothing to revoke.
    /// </summary>
    [Fact]
    public async Task A_non_human_session_is_not_active_and_is_left_alone()
    {
        var admin = await _seed.CallerAsync("ses-c4-administrator", "user-administrator");
        var (agent, session) = await _seed.AgentWithSessionAsync();
        var before = await _seed.FootprintAsync([session]);

        await RevokeAsync(admin.UserId, agent);

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task An_unknown_user_is_refused()
    {
        var admin = await _seed.CallerAsync("ses-c4-administrator", "user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(admin.UserId, UserId.New()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var admin = await _seed.CallerAsync("ses-c4-administrator", "user-administrator");
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(admin.UserId, holder.UserId, reason));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Theory]
    [InlineData("access-reviewer")]
    [InlineData(null)]
    public async Task A_caller_without_session_revoke_is_refused(string? role)
    {
        var caller = await _seed.CallerAsync($"ses-c4-{role ?? "unprivileged"}", role);
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(caller.UserId, holder.UserId));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => RevokeAsync(caller: null, holder.UserId));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    /// <summary>D3 — the seed is followed: an agent holding session.revoke is allowed.</summary>
    [Fact]
    public async Task An_agent_holding_session_revoke_is_allowed()
    {
        var agent = await _seed.AgentRevokerAsync();
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);

        await RevokeAsync(agent, holder.UserId, actorType: ActorType.Agent);

        Assert.Equal(agent.Value, (await _seed.ReadSessionAsync(session)).RevokedBy);
    }

    private Task RevokeAsync(
        UserId? caller, UserId target, string reason = Explanation, ActorType actorType = ActorType.Human)
        => _seed.DispatchAsync<RevokeUserSessionsCommand, RevokeUserSessionsResult>(
            caller, new RevokeUserSessionsCommand(target, reason), actorType);
}
