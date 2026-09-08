using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// The authoritative per-request session check (docs/architecture.md
/// section 17).
///
/// D7 chose server-side sessions over self-contained tokens, and the
/// consequence is this class: EVERY authenticated request consults the
/// database. A token carrying its own claims could not honour "a deactivation
/// takes effect immediately" — it would take effect whenever the token lapsed.
/// </summary>
public sealed class CallerEstablisher : ICallerEstablisher
{
    /// <summary>
    /// Closes open decision A6. An enforcement and write-recognition
    /// tolerance, NOT an extension of the configured timeout: with a 30-minute
    /// SessionIdleTimeout the configured timeout is still 30 minutes, but a
    /// request arriving shortly after may be accepted because the stored
    /// activity value is throttled.
    ///
    /// US7 sets the direction — the effective idle timeout may exceed the
    /// configured value by the documented tolerance but must never fall short.
    /// A genuinely active user is never signed out early.
    /// </summary>
    public static readonly TimeSpan EnforcementTolerance =
        TimeSpan.FromSeconds(60);

    /// <summary>
    /// Deliberately the same 60 seconds, because it is the same phenomenon:
    /// the tolerance exists precisely to absorb this throttle. A staleness
    /// threshold LARGER than the tolerance would breach US7 — the stored value
    /// could lag by more than enforcement forgives, and a genuinely active user
    /// would be signed out early.
    /// </summary>
    public static readonly TimeSpan ActivityStaleness =
        TimeSpan.FromSeconds(60);

    private readonly IClock _clock;
    private readonly LigatureDbContext _dbContext;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IExecutionContextInitializer _executionContextInitializer;

    public CallerEstablisher(
        IClock clock,
        LigatureDbContext dbContext,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IExecutionContextInitializer executionContextInitializer)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _clock = clock;
        _dbContext = dbContext;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _executionContextInitializer = executionContextInitializer;
    }

    /// <inheritdoc />
    public async Task<bool> EstablishAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var now = _clock.UtcNow;

        var snapshot = await LoadAsync(sessionId, cancellationToken);

        // Unknown session. Indistinguishable from every other rejection below.
        if (snapshot is null)
            return false;

        var policy = await _securityPolicyResolver
            .GetEffectiveSettingsAsync(now, cancellationToken);

        // Not revoked, within absolute expiry, within the idle window. The
        // domain predicate is reused rather than restated here, so there is one
        // definition of session validity; the tolerance widens the idle window
        // only, which is exactly what section 17 describes it as.
        if (!snapshot.Session.IsActive(
                now, policy.SessionIdleTimeout + EnforcementTolerance))
        {
            return false;
        }

        // Both must still be active, and this is the whole reason the check is
        // per-request: it is what lets a deactivation take effect on the very
        // next request rather than whenever a token would have lapsed.
        if (snapshot.IdentityStatus != UserStatus.Active)
            return false;

        if (snapshot.UserStatus != UserStatus.Active)
            return false;

        // Inv. 26 — sessions exist for interactive human authentication, so a
        // session held by anything else is a model violation. Refused rather
        // than thrown: this is middleware, and it must not be the one component
        // that answers differently. ScopedExecutionContext.Establish would also
        // throw on the System actor, and an exception must not reach a client
        // from here.
        if (snapshot.ActorType != ActorType.Human)
            return false;

        // VALIDITY IS EVALUATED BEFORE ACTIVITY IS RECORDED, and every return
        // above happens before the write. Reverse the two and an already-idle
        // session would resurrect itself simply by making one more request —
        // the idle check reads last_activity_at, so writing it first would
        // always satisfy the check that was meant to reject.
        _executionContextInitializer.Establish(
            snapshot.UserId, snapshot.ActorType);

        await _userSessionRepository.RecordActivityAsync(
            sessionId, now, ActivityStaleness, cancellationToken);

        return true;
    }

    /// <summary>
    /// One query, and NO TRACKING — deliberately.
    ///
    /// A tracked read here would put the session, identity and user into the
    /// request scope's change tracker before the command pipeline ever runs,
    /// and the handler that later loads the same session would receive this
    /// instance rather than a fresh one. That matters because UnitOfWork wraps
    /// handlers in an execution strategy that may RETRY: on a second attempt
    /// the entity would still carry the first attempt's mutations, and
    /// UserSession.Revoke — which returns false when already revoked — would
    /// silently no-op. Leaving the tracker empty costs one extra query in the
    /// handler and removes that class of bug entirely.
    /// </summary>
    private async Task<Snapshot?> LoadAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken)
    {
        return await (
            from session in _dbContext.Set<UserSession>().AsNoTracking()
            join identity in _dbContext.Set<UserIdentity>().AsNoTracking()
                on session.UserIdentityId equals identity.Id
            join user in _dbContext.Set<User>().AsNoTracking()
                on identity.UserId equals user.Id
            where session.Id == sessionId
            select new Snapshot(
                session,
                identity.Status,
                user.Status,
                user.Id,
                user.ActorType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The session plus the two statuses that make it usable. An inner join is
    /// correct rather than convenient: a session whose identity or user row is
    /// missing is not a session anyone may act under, and it collapses into the
    /// same "no snapshot" rejection.
    /// </summary>
    private sealed record Snapshot(
        UserSession Session,
        UserStatus IdentityStatus,
        UserStatus UserStatus,
        UserId UserId,
        ActorType ActorType);
}
