using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.ReactivateRole;

/// <summary>
/// AUT-C6, the inverse of AUT-C5. NO REASON (RD5): the catalogue gives this
/// command none and RoleReactivated is seeded ReasonRequired: false. One is
/// not added for symmetry.
/// </summary>
public sealed record ReactivateRoleCommand(RoleId RoleId)
    : IAuthorizableCommand<ReactivateRoleResult>,
      IHumanActorOnlyCommand<ReactivateRoleResult>
{
    public string RequiredPermission => "role.manage";
}
