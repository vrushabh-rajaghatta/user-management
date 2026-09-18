using Ligature.Platform.Application.Users.Queries.RoleAssignments;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>AUT-Q2's read of stored assignments.</summary>
public interface IUserRoleAssignmentReader
{
    /// <summary>
    /// Every assignment of the user, as stored, with the role's and the
    /// administrators' names; empty for a user with none, null for a user that
    /// does not exist. No state: that is derived by the handler.
    /// </summary>
    Task<IReadOnlyList<UserRoleAssignmentRecord>?> ReadAsync(
        UserId userId,
        CancellationToken cancellationToken);
}
