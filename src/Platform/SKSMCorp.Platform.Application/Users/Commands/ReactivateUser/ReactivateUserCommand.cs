using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.ReactivateUser;

/// <summary>
/// USR-C5 (docs/requirements.md, "USR-C4 / USR-C5"). Returns a deactivated
/// person to the tenant, restoring nothing.
/// </summary>
/// <param name="Reason">Required: the reason on every audit record.</param>
public sealed record ReactivateUserCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<ReactivateUserResult>,
      IHumanActorOnlyCommand<ReactivateUserResult>
{
    public string RequiredPermission => "user.reactivate";
}
