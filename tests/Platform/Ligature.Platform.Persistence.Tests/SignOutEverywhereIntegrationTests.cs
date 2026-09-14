using Ligature.Platform.Application.Users.Commands.SignOutEverywhere;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Kind = Ligature.Platform.Persistence.Tests.SessionRevocationSeed.Kind;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C4, self form, end to end. The caller is the account holder; every
/// session ended is the caller's own, with the code SignOutEverywhere and the
/// caller's explanation — or the fixed one — as the audit Reason. SignedOut is
/// never emitted.
/// </summary>
public sealed class SignOutEverywhereIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Default = "Signed out of all sessions by the account holder";

    private readonly SessionRevocationSeed _seed;

    public SignOutEverywhereIntegrationTests(ActivationDatabase database)
        => _seed = new SessionRevocationSeed(database);

    [Fact]
    public async Task By_default_every_active_session_is_ended_including_this_one()
    {
        var holder = await _seed.PersonAsync();
        var external = await _seed.IdentityAsync(holder.UserId, external: true);

        var current = await _seed.SessionAsync(holder.LocalIdentityId);
        var otherLocal = await _seed.SessionAsync(holder.LocalIdentityId);
        var otherExternal = await _seed.SessionAsync(external, Kind.IdleWithinTolerance);
        var pastTolerance = await _seed.SessionAsync(external, Kind.IdlePastTolerance);
        var expired = await _seed.SessionAsync(holder.LocalIdentityId, Kind.Expired);

        var stranger = await _seed.PersonAsync();
        var strangersSession = await _seed.SessionAsync(stranger.LocalIdentityId);

        var untouched = new[] { pastTolerance, expired, strangersSession };
        var before = await _seed.FootprintAsync(untouched);

        await SignOutAsync(holder.UserId, current, keep: false, reason: null);

        foreach (var session in new[] { current, otherLocal, otherExternal })
        {
            var row = await _seed.ReadSessionAsync(session);

            Assert.True(row.IsRevoked);
            Assert.Equal(holder.UserId.Value, row.RevokedBy);
            Assert.Equal("SignOutEverywhere", row.Reason);

            var record = Assert.Single(await _seed.ReadRevocationsAsync(session));

            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(holder.UserId.Value, record.ActorUserId);
            Assert.Equal(Default, record.Reason);
            Assert.Equal("SignOutEverywhere", record.Code);
            Assert.Equal(holder.UserId.Value, record.SubjectUser);
        }

        Assert.Equal(before, await _seed.FootprintAsync(untouched));

        Assert.Equal(0, await _seed.CountSignedOutAsync([current, otherLocal, otherExternal]));
    }

    [Fact]
    public async Task Keeping_the_current_session_ends_only_the_others()
    {
        var holder = await _seed.PersonAsync();
        var current = await _seed.SessionAsync(holder.LocalIdentityId);
        var other = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([current]);

        await SignOutAsync(holder.UserId, current, keep: true, reason: null);

        Assert.Equal(before, await _seed.FootprintAsync([current]));
        Assert.True((await _seed.ReadSessionAsync(other)).IsRevoked);
    }

    [Theory]
    [InlineData("Lost my phone on the train.", "Lost my phone on the train.")]
    [InlineData("   ", Default)]
    [InlineData("", Default)]
    public async Task The_explanation_is_the_callers_or_the_fixed_default(string reason, string expected)
    {
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);

        await SignOutAsync(holder.UserId, session, keep: false, reason);

        Assert.Equal(expected, Assert.Single(await _seed.ReadRevocationsAsync(session)).Reason);
    }

    [Fact]
    public async Task With_no_active_sessions_it_is_a_no_op()
    {
        var holder = await _seed.PersonAsync();
        var ended = new[]
        {
            await _seed.SessionAsync(holder.LocalIdentityId, Kind.Revoked),
            await _seed.SessionAsync(holder.LocalIdentityId, Kind.Expired),
        };

        var before = await _seed.FootprintAsync(ended);

        await SignOutAsync(holder.UserId, ended[0], keep: false, reason: null);

        Assert.Equal(before, await _seed.FootprintAsync(ended));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => SignOutAsync(caller: null, session, keep: false, reason: null));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        var holder = await _seed.PersonAsync();
        var session = await _seed.SessionAsync(holder.LocalIdentityId);
        var before = await _seed.FootprintAsync([session]);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => SignOutAsync(holder.UserId, session, keep: false, reason: null, ActorType.Agent));

        Assert.Equal(before, await _seed.FootprintAsync([session]));
    }

    private Task SignOutAsync(
        UserId? caller, Guid current, bool keep, string? reason, ActorType actorType = ActorType.Human)
        => _seed.DispatchAsync<SignOutEverywhereCommand, SignOutEverywhereResult>(
            caller,
            new SignOutEverywhereCommand(new UserSessionId(current), keep, reason),
            actorType);
}
