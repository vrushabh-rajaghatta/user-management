using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.DeactivateUser;

/// <summary>
/// USR-C4 (docs/requirements.md, "USR-C4 / USR-C5"). An administrator takes a
/// person out of the tenant in one controlled operation.
/// </summary>
/// <param name="Reason">Required: the reason on every audit record of the cascade.</param>
public sealed record DeactivateUserCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<DeactivateUserResult>,
      IHumanActorOnlyCommand<DeactivateUserResult>
{
    public string RequiredPermission => "user.deactivate";
}
