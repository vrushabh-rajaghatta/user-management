using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.RevokeRole;

/// <summary>
/// AUT-C2. An administrator ends a role assignment, addressed by the
/// assignment itself: a user may hold the same role in several periods over
/// time, and the assignment is the thing acted on.
/// </summary>
/// <param name="Reason">Required: the RevocationReason and the audit record's reason.</param>
public sealed record RevokeRoleCommand(
    UserRoleId AssignmentId,
    string Reason)
    : IAuthorizableCommand<RevokeRoleResult>,
      IHumanActorOnlyCommand<RevokeRoleResult>
{
    public string RequiredPermission => "role.revoke";
}
