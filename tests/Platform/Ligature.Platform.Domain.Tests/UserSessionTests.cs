using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// IsActive is the single definition of session validity. Caller establishment
/// reuses it rather than restating it (docs/architecture.md section 17), which
/// is only safe if each of its three clauses is load-bearing — so each is
/// tested on its own here.
/// </summary>
public sealed class UserSessionTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    [Fact]
    public void A_fresh_session_is_active()
        => Assert.True(Session().IsActive(Start, IdleTimeout));

    [Fact]
    public void A_revoked_session_is_not_active()
    {
        var session = Session();

        session.Revoke(Start, UserId.New(), "Logout");

        // Still inside both windows — revocation alone ends it.
        Assert.False(session.IsActive(Start, IdleTimeout));
    }

    /// <summary>
    /// A wide idle window, so this exercises the EXPIRY clause alone. With the
    /// real 15-minute timeout the session is idle long before it expires, and
    /// the assertion would pass without the expiry check existing at all.
    /// </summary>
    [Fact]
    public void A_session_at_its_absolute_expiry_is_not_active()
    {
        var session = Session();

        var neverIdle = TimeSpan.FromDays(365);

        // The boundary is exclusive: at expiry it is over, not still running.
        Assert.False(session.IsActive(Start.AddHours(12), neverIdle));

        Assert.True(
            session.IsActive(Start.AddHours(12).AddSeconds(-1), neverIdle));
    }

    [Fact]
    public void A_session_idle_beyond_the_timeout_is_not_active()
    {
        var session = Session();

        Assert.False(session.IsActive(Start + IdleTimeout, IdleTimeout));

        Assert.True(
            session.IsActive(
                Start + IdleTimeout - TimeSpan.FromSeconds(1), IdleTimeout));
    }

    /// <summary>
    /// The idle window is measured from the LAST ACTIVITY, not from creation —
    /// which is what makes recording activity mean anything at all.
    /// </summary>
    [Fact]
    public void Recording_activity_moves_the_idle_window()
    {
        var session = Session();

        session.RecordActivity(Start.AddMinutes(10));

        // Twenty minutes after creation, but only ten after activity.
        Assert.True(session.IsActive(Start.AddMinutes(20), IdleTimeout));
    }

    /// <summary>
    /// A widened window is exactly how the section 17 enforcement tolerance is
    /// applied: the idle boundary moves, and nothing else does. Passing a
    /// larger timeout must not resurrect a session that is over on either of
    /// the other two counts.
    /// </summary>
    [Fact]
    public void A_wider_idle_window_does_not_override_expiry_or_revocation()
    {
        var expired = Session();

        Assert.False(
            expired.IsActive(Start.AddHours(13), TimeSpan.FromDays(365)));

        var revoked = Session();
        revoked.Revoke(Start, UserId.New(), "Logout");

        Assert.False(revoked.IsActive(Start, TimeSpan.FromDays(365)));
    }

    [Fact]
    public void Activity_cannot_move_backwards()
    {
        var session = Session();

        session.RecordActivity(Start.AddMinutes(10));

        Assert.Throws<DomainException>(
            () => session.RecordActivity(Start.AddMinutes(5)));
    }

    private static UserSession Session()
        => UserSession.Create(
            UserSessionId.New(),
            UserIdentityId.New(),
            Start,
            Start.AddHours(12),
            ipAddress: null,
            userAgent: "domain-tests/1.0");
}
