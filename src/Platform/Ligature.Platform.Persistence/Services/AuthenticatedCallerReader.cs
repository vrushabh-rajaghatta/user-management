using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// The session's identity and timing, in one read (B6-B).
///
/// The same join CallerEstablisher performs, for a different purpose and
/// through its own abstraction. Sharing that class would tie an API read model
/// to the authentication boundary's internals, and the two answer different
/// questions: the establisher asks whether this request may proceed, and this
/// asks who the caller it accepted is.
///
/// UserIdentityId comes from the SESSION, never from picking an identity that
/// belongs to the user: a user may hold several, and reporting another would
/// answer a different question (D3).
///
/// AsNoTracking, as the establisher is, so nothing reaches the change tracker.
/// </summary>
public sealed class AuthenticatedCallerReader : IAuthenticatedCallerReader
{
    private readonly LigatureDbContext _dbContext;

    public AuthenticatedCallerReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<AuthenticatedCaller?> ReadAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        return await (
            from session in _dbContext.Set<UserSession>().AsNoTracking()
            join identity in _dbContext.Set<UserIdentity>().AsNoTracking()
                on session.UserIdentityId equals identity.Id
            join user in _dbContext.Set<User>().AsNoTracking()
                on identity.UserId equals user.Id
            where session.Id == sessionId
            select new AuthenticatedCaller(
                identity.Id,
                identity.Username,
                user.DisplayName,
                session.ExpiresAt,
                session.LastActivityAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
