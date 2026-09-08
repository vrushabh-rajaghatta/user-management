using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

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
    Task RecordActivityAsync(
        UserSessionId sessionId,
        DateTimeOffset now,
        TimeSpan staleness,
        CancellationToken cancellationToken);
}
