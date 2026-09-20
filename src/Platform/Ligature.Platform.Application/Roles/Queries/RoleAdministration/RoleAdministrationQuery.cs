using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RoleAdministration;

/// <summary>
/// AUT-Q5 ListRoles (docs/requirements.md, "Role administration read", RA1-RA4),
/// under role.read. Both parameters filter the result set only.
/// </summary>
public sealed record RoleAdministrationQuery(bool IncludeInactive, bool AgentAssignableOnly)
    : IQuery<RoleAdministrationResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
