using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>
/// AUT-Q2 — GetUserRoleAssignments. RED STUB: the contract is in
/// docs/requirements.md ("AUT-Q2"), and this exists only so its tests compile.
/// </summary>
public sealed record UserRoleAssignmentsQuery(UserId UserId, bool IncludeInactive)
    : IQuery<UserRoleAssignmentsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization
        => throw new NotImplementedException("AUT-Q2 is not implemented.");
}
