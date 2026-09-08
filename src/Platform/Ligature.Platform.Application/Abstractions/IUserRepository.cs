using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserRepository
{
    Task<bool> ExistsActiveHumanWithEmailAsync(
        EmailAddress email,
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
}