using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>Role assignments (AUT-C1, AUT-C2).</summary>
public interface IUserRoleRepository
{
    /// <summary>Adds to the change tracker only; UnitOfWork owns the save.</summary>
    Task AddAsync(
        UserRole assignment,
        CancellationToken cancellationToken);

    /// <summary>Tracked, so a revocation is saved with the unit of work.</summary>
    Task<UserRole?> FindAsync(
        UserRoleId assignmentId,
        CancellationToken cancellationToken);
}
