using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

public interface IUserSessionRepository
{
    Task AddAsync(UserSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a session by id, or null (SES-C2).
    ///
    /// TRACKED, because the caller revokes through it and UnitOfWork commits
    /// that write with the rest of the operation.
    /// </summary>
    Task<UserSession?> FindAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records authenticated-request activity, throttled and monotonic
    /// (docs/architecture.md section 17).
    ///
    /// Writes only when the STORED value is already staler than
    /// <paramref name="staleness"/>. That single predicate delivers both
    /// properties: because it compares against the value currently in the row
    /// rather than against anything the caller holds, a delayed request cannot
    /// move the timestamp backwards — its own instant will not be far enough
    /// ahead of a fresher stored value to satisfy the condition.
    ///
    /// Independently persisted, and NOT part of the caller's transaction:
    /// section 17 requires that establishment and the activity write not be
    /// assumed to share one. Nothing is returned, because whether the throttle
    /// let this particular request through is not information any caller
    /// should act on.
    /// </summary>
    /// <summary>
    /// CRD-C4 (A5). Every session of <paramref name="identityId"/> OTHER than
    /// <paramref name="excluding"/> that would still pass the per-request
    /// session check at <paramref name="now"/> — tracked, so the caller revokes
    /// through these instances.
    ///
    /// "Would still pass" is not restated here: it is UserSession.IsActive with
    /// the idle timeout widened by the same enforcement tolerance the
    /// per-request check applies. A session that check would already refuse is
    /// ended, and revoking it would record a termination that changed nothing.
    ///
    /// The caller passes the effective idle timeout; the tolerance is added by
    /// the implementation, beside the check that owns it, so the two cannot
    /// drift apart.
    /// </summary>
    Task<IReadOnlyList<UserSession>> FindOtherActiveForIdentityAsync(
        UserIdentityId identityId,
        UserSessionId excluding,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// SES-C3. The session if it passes the CANONICAL active-session test at
    /// <paramref name="now"/>, otherwise null — tracked, so the caller revokes
    /// through it.
    ///
    /// Canonical means exactly what the per-request session check accepts: not
    /// revoked, before absolute expiry, within the idle timeout widened by the
    /// enforcement tolerance, held by an Active identity of an Active Human
    /// user. The caller passes the effective idle timeout; the tolerance and the
    /// status joins are the implementation's, beside the check that owns them.
    /// </summary>
    Task<UserSession?> FindActiveAsync(
        UserSessionId sessionId,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// SES-C4. Every session of EVERY identity of <paramref name="userId"/> that
    /// passes the canonical active-session test (see FindActiveAsync) — tracked.
    /// </summary>
    Task<IReadOnlyList<UserSession>> FindActiveForUserAsync(
        UserId userId,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken);

    Task RecordActivityAsync(
        UserSessionId sessionId,
        DateTimeOffset now,
        TimeSpan staleness,
        CancellationToken cancellationToken);
}
