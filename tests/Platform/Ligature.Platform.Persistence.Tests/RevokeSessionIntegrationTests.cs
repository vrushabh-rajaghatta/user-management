using Ligature.Platform.Application.Users.Commands.RevokeSession;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Kind = Ligature.Platform.Persistence.Tests.SessionRevocationSeed.Kind;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C3 end to end. Every no-op and refusal is compared against the session's
/// footprint — its revocation columns AND any SessionRevoked naming it — so a
/// path that wrote an event it then rolled back, or revoked without recording,
/// cannot pass.
/// </summary>
public sealed class RevokeSessionIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Explanation = "SUP-9001: session opened from an unrecognised network.";

    private readonly SessionRevocationSeed _seed;

    public RevokeSessionIntegrationTests(ActivationDatabase database)
        => _seed = new SessionRevocationSeed(database);

    [Theory]
    [InlineData(nameof(Kind.Active))]
    [InlineData(nameof(Kind.IdleWithinTolerance))]
    public async Task An_active_session_is_revoked_with_the_code_and_the_explanation(string kind)
    {
        var admin = await _seed.CallerAsync("ses-c3-administrator", "user-administrator");
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId, Enum.Parse<Kind>(kind));

        await RevokeAsync(admin.UserId, session);

        var row = await _seed.ReadSessionAsync(session);

        Assert.True(row.IsRevoked);
        Assert.Equal(admin.UserId.Value, row.RevokedBy);
        Assert.Equal("AdminRevoked", row.Reason);

        var record = Assert.Single(await _seed.ReadRevocationsAsync(session));

        Assert.Equal("Authenticated", record.OriginKind);
        Assert.Equal(admin.UserId.Value, record.ActorUserId);
        Assert.Equal("Session", record.EntityType);

        // D2 — the explanation is the Reason; the code rides in After.
        Assert.Equal(Explanation, record.Reason);
        Assert.Equal("AdminRevoked", record.Code);

        Assert.Equal(holder.LocalIdentityId, record.TargetIdentity);
        Assert.Equal(holder.UserId.Value, record.SubjectUser);
    }

    /// <summary>
    /// D6 — anything failing the canonical active-session test is already over:
    /// 204, nothing written, nothing declared.
    /// </summary>
    [Theory]
    [InlineData(nameof(Kind.IdlePastTolerance), false, false)]
    [InlineData(nameof(Kind.Revoked), false, false)]
    [InlineData(nameof(Kind.Expired), false, false)]
    [InlineData(nameof(Kind.ExpiredRecentlyActive), false, false)]
    [InlineData(nameof(Kind.Active), true, false)]
    [InlineData(nameof(Kind.Active), false, true)]
    public async Task An_ended_session_is_a_no_op(string kind, bool inactiveIdentity, bool inactiveUser)
    {
        var admin = await _seed.CallerAsync("ses-c3-administrator", "user-administrator");
        var holder = await _seed.PersonAsync(userActive: !inactiveUser);
        var session = await _seed.SessionAsync(holder.LocalIdentityId, Enum.Parse<Kind>(kind));

        if (inactiveIdentity)
            await _seed.DeactivateIdentityAsync(holder.LocalIdentityId);

        var before = await _seed.FootprintAsync([session]);

        await RevokeAsync(admin.UserId, session);

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task An_unknown_session_is_refused()
    {
        var admin = await _seed.CallerAsync("ses-c3-administrator", "user-administrator");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(admin.UserId, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var admin = await _seed.CallerAsync("ses-c3-administrator", "user-administrator");
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(admin.UserId, session, reason));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task An_administrator_may_revoke_their_own_session()
    {
        var admin = await _seed.CallerAsync("ses-c3-self-administrator", "user-administrator");
        var own = await _seed.SessionAsync(admin.IdentityId.Value);

        await RevokeAsync(admin.UserId, own);

        Assert.True((await _seed.ReadSessionAsync(own)).IsRevoked);
    }

    // ---- callers

    [Theory]
    [InlineData("access-reviewer")]
    [InlineData(null)]
    public async Task A_caller_without_session_revoke_is_refused(string? role)
    {
        var caller = await _seed.CallerAsync($"ses-c3-{role ?? "unprivileged"}", role);
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => RevokeAsync(caller.UserId, session));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => RevokeAsync(caller: null, session));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    /// <summary>
    /// D3 — session.revoke is not human-only, and nothing here makes it so. An
    /// agent legitimately holding it revokes, and the record names the agent.
    /// </summary>
    [Fact]
    public async Task An_agent_holding_session_revoke_is_allowed()
    {
        var agent = await _seed.AgentRevokerAsync();
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);

        await RevokeAsync(agent, session, actorType: ActorType.Agent);

        Assert.Equal(agent.Value, (await _seed.ReadSessionAsync(session)).RevokedBy);
        Assert.Equal(agent.Value, Assert.Single(await _seed.ReadRevocationsAsync(session)).ActorUserId);
    }

    private Task RevokeAsync(
        UserId? caller, Guid session, string reason = Explanation, ActorType actorType = ActorType.Human)
        => _seed.DispatchAsync<RevokeSessionCommand, RevokeSessionResult>(
            caller, new RevokeSessionCommand(new UserSessionId(session), reason), actorType);
}
