using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.RemovePermissionFromRole;

/// <summary>
/// AUT-C8. An administrator takes a permission away from a TENANT role. The
/// grant is CLOSED, never deleted (invariant 11).
///
/// THE REASON IS REQUIRED (RG5) and lands only in the audit record: the frozen
/// role_permission entity has no column for it.
/// </summary>
public sealed record RemovePermissionFromRoleCommand(
    RolePermissionId RolePermissionId,
    string Reason)
    : IAuthorizableCommand<RemovePermissionFromRoleResult>,
      IHumanActorOnlyCommand<RemovePermissionFromRoleResult>
{
    public string RequiredPermission => "role.manage";
}
