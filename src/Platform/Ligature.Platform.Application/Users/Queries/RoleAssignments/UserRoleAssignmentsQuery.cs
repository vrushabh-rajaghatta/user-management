using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>
/// AUT-Q2 — GetUserRoleAssignments (docs/requirements.md, "AUT-Q2").
/// </summary>
/// <param name="IncludeInactive">
/// False: Active and Future assignments. True: those AND Ended and Revoked.
/// Not "only inactive", and not a stored flag: it filters on the state derived
/// at the instant of the read.
/// </param>
public sealed record UserRoleAssignmentsQuery(UserId UserId, bool IncludeInactive)
    : IQuery<UserRoleAssignmentsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
