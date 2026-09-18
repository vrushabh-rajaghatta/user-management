using Ligature.Platform.Application.Users.Queries.RoleAssignments;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>AUT-Q2. RED STUB.</summary>
public interface IUserRoleAssignmentReader
{
    /// <summary>Every assignment of the user, or null when the user does not exist.</summary>
    Task<IReadOnlyList<UserRoleAssignmentRecord>?> ReadAsync(
        UserId userId,
        CancellationToken cancellationToken);
}
