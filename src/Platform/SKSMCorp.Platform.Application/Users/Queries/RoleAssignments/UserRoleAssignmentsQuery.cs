using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.RoleAssignments;

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
