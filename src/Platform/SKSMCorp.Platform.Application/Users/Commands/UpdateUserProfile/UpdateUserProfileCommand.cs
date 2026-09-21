using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.UpdateUserProfile;

/// <summary>
/// USR-C2, the administrator command (docs/requirements.md, "USR-C2 — Update
/// User Profile"). First, last and display name only; never email or status,
/// which have their own commands. No reason: the catalogue has none.
///
/// NOT human-only: the catalogue does not mark user.update so.
/// </summary>
public sealed record UpdateUserProfileCommand(
    UserId UserId,
    string FirstName,
    string LastName,
    string DisplayName)
    : IAuthorizableCommand<UpdateUserProfileResult>
{
    public string RequiredPermission => "user.update";
}
