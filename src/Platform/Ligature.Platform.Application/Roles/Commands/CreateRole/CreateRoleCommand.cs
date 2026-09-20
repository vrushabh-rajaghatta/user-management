using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Roles.Commands.CreateRole;

/// <summary>
/// AUT-C3 CreateRole (docs/requirements.md, "AUT-C3 CreateRole — the tenant
/// role, and its dialog"), under role.manage, human actors only.
///
/// RC9: there is deliberately NO IsSystemRole here. The domain sets it false,
/// so this command cannot create a release-owned role whatever a caller sends.
/// </summary>
public sealed record CreateRoleCommand(
    string Code,
    string Name,
    string? Description)
    : IAuthorizableCommand<CreateRoleResult>,
      IHumanActorOnlyCommand<CreateRoleResult>
{
    public string RequiredPermission => "role.manage";
}
