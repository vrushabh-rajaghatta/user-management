using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.DeactivateRole;

/// <summary>
/// AUT-C5 (docs/requirements.md, "AUT-C5 DeactivateRole and AUT-C6
/// ReactivateRole"). An administrator retires a TENANT role.
///
/// THE REASON IS REQUIRED (RD5): the catalogue gives this command one, and
/// RoleDeactivated is seeded ReasonRequired. It is refused before any database
/// work, because AR9 would otherwise turn a missing one into a 500.
/// </summary>
public sealed record DeactivateRoleCommand(
    RoleId RoleId,
    string Reason)
    : IAuthorizableCommand<DeactivateRoleResult>,
      IHumanActorOnlyCommand<DeactivateRoleResult>
{
    public string RequiredPermission => "role.manage";
}
