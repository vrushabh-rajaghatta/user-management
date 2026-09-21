using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.ChangeUserEmail;

/// <summary>
/// USR-C3, the administrator command (docs/requirements.md, "USR-C3
/// ChangeUserEmail"). The reason is optional, as the catalogue says.
///
/// NOT human-only, like USR-C2 under the same permission: the catalogue's
/// "Human actors only" is about the target (CE8).
/// </summary>
public sealed record ChangeUserEmailCommand(
    UserId UserId,
    string Email,
    string? Reason)
    : IAuthorizableCommand<ChangeUserEmailResult>
{
    public string RequiredPermission => "user.update";
}
