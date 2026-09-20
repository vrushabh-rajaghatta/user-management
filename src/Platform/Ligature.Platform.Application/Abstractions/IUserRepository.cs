using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserRepository
{
    Task<bool> ExistsActiveHumanWithEmailAsync(
        EmailAddress email,
        CancellationToken cancellationToken);

    /// <summary>
    /// USR-C3 (CE5): as <see cref="ExistsActiveHumanWithEmailAsync"/>, but
    /// ignoring one user — the target of an email change, whose own address is
    /// not a collision with itself.
    /// </summary>
    Task<bool> ExistsOtherActiveHumanWithEmailAsync(
        EmailAddress email,
        UserId excluding,
        CancellationToken cancellationToken);

    Task AddAsync(
        User user,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads an actor by id, or null (SES-C1 step 2).
    /// </summary>
    Task<User?> FindAsync(
        UserId userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads a user under a row lock (SELECT ... FOR UPDATE), tracked, inside
    /// the caller's transaction. USR-C4, USR-C5 and AUT-C1 take this lock
    /// before checking status, so a grant cannot race a deactivation (D6).
    /// </summary>
    Task<User?> FindForUpdateAsync(
        UserId userId,
        CancellationToken cancellationToken);
}
