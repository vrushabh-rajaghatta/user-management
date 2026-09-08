using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

/// <summary>
/// user_session is the one table exempt from the no-hard-delete rule (G1):
/// terminated rows are operational state, purgeable after retention, because
/// the audit trail holds the authoritative sign-in history. Nothing here
/// deletes; the purge belongs to a separate role.
/// </summary>
public sealed class UserSessionRepository : IUserSessionRepository
{
    private readonly LigatureDbContext _dbContext;

    public UserSessionRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task AddAsync(UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        _dbContext.Add(session);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<UserSession?> FindAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        // Tracked: SES-C2 revokes through this instance.
        return await _dbContext.Set<UserSession>()
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordActivityAsync(
        UserSessionId sessionId,
        DateTimeOffset now,
        TimeSpan staleness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (staleness <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staleness),
                staleness,
                "The staleness threshold must be positive. A zero or negative "
                + "threshold would write on every request, which is the "
                + "throttle this method exists to apply.");
        }

        // Raw SQL, and a conditional UPDATE rather than load-mutate-save, for
        // the same reason UserTokenRepository.TryConsumeAsync is: the guard and
        // the write have to be one statement. Read-then-write would leave a
        // window in which two concurrent requests both decide to write, and the
        // later-arriving one could carry the earlier instant.
        //
        // last_activity_at < @threshold IS the monotonicity guarantee, not just
        // the throttle. The comparison is against the stored value, so a
        // request whose instant is behind a fresher stored value simply fails
        // the condition and writes nothing.
        //
        // The revoked and expiry guards are defence in depth: establishment has
        // already validated the session, but it did so outside this statement,
        // and activity must never be written for a session that has since
        // ended.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE user_session
             SET last_activity_at = {now}
             WHERE id = {sessionId.Value}
               AND last_activity_at < {now - staleness}
               AND revoked_at IS NULL
               AND expires_at > {now}
             """,
            cancellationToken);
    }
}
