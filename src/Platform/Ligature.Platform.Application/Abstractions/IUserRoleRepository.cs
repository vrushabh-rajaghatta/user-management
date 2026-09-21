using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>Role assignments (AUT-C1, AUT-C2).</summary>
public interface IUserRoleRepository
{
    /// <summary>
    /// AUT-C7 (RP6): whether this role is held by an AGENT under an assignment
    /// that is ACTIVE at the given instant — not revoked, and within its
    /// half-open effective period.
    ///
    /// "Active" is the frozen wording and is implemented literally (RG3): a
    /// FUTURE agent assignment does not count, which is a recorded gap rather
    /// than something this method quietly widens.
    /// </summary>
    Task<bool> HasActiveAgentAssignmentAsync(
        RoleId roleId,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>Adds to the change tracker only; UnitOfWork owns the save.</summary>
    Task AddAsync(
        UserRole assignment,
        CancellationToken cancellationToken);

    /// <summary>Tracked, so a revocation is saved with the unit of work.</summary>
    Task<UserRole?> FindAsync(
        UserRoleId assignmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every assignment the user holds, whatever its state, tracked. USR-C4
    /// passes each to UserRole.Revoke, which decides what revocation means.
    /// </summary>
    Task<IReadOnlyList<UserRole>> FindForUserAsync(
        UserId userId,
        CancellationToken cancellationToken);
}
