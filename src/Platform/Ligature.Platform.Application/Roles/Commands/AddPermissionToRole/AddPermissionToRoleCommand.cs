using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Roles.Commands.AddPermissionToRole;

/// <summary>
/// AUT-C7 (docs/requirements.md, "AUT-C7 AddPermissionToRole and AUT-C8
/// RemovePermissionFromRole"). An administrator adds a permission to a TENANT
/// role.
///
/// NO REASON (RG5): the catalogue gives this command none and
/// PermissionGrantedToRole is seeded ReasonRequired: false.
/// </summary>
public sealed record AddPermissionToRoleCommand(
    RoleId RoleId,
    PermissionId PermissionId)
    : IAuthorizableCommand<AddPermissionToRoleResult>,
      IHumanActorOnlyCommand<AddPermissionToRoleResult>
{
    public string RequiredPermission => "role.manage";
}
