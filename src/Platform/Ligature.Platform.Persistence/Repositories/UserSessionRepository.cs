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
}
