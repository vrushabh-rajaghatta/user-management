using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.UpdateRoleMetadata;

/// <summary>
/// AUT-C4 (docs/requirements.md, "AUT-C4 UpdateRoleMetadata"). An administrator
/// changes a TENANT role's name and description.
///
/// THE CODE IS NOT AN INPUT (RM2), and neither is IsSystemRole (RM10). The
/// frozen command's inputs are RoleId, Name and Description; there is no
/// legitimate request carrying a code, so there is none to refuse.
/// </summary>
public sealed record UpdateRoleMetadataCommand(
    RoleId RoleId,
    string Name,
    string? Description)
    : IAuthorizableCommand<UpdateRoleMetadataResult>,
      IHumanActorOnlyCommand<UpdateRoleMetadataResult>
{
    public string RequiredPermission => "role.manage";
}
