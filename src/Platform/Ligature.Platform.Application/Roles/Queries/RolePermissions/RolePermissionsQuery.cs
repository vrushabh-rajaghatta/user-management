using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RolePermissions;

/// <summary>
/// AUT-Q3 GetRolePermissions (docs/requirements.md, "Role administration read",
/// RA5, RA11), under role.read. AsOf is null for the current instant.
/// </summary>
public sealed record RolePermissionsQuery(RoleId RoleId, DateTimeOffset? AsOf)
    : IQuery<RolePermissionsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
