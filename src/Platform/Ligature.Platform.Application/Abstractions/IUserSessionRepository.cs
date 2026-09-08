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
}
