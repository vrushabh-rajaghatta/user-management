using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Roles.Queries.RolePermissions;

/// <summary>
/// AUT-Q3 GetRolePermissions (docs/requirements.md, "Role administration read",
/// RA5, RA11), under role.read. AsOf is null for the current instant.
/// </summary>
public sealed record RolePermissionsQuery(RoleId RoleId, DateTimeOffset? AsOf)
    : IQuery<RolePermissionsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
