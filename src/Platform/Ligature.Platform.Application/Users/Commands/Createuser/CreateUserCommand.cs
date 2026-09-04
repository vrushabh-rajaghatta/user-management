using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.CreateUser;

public sealed record CreateUserCommand(
    string FirstName,
    string LastName,
    string DisplayName,
    string Email,
    string InitialUsername)
    : IAuthorizableCommand<CreateUserResult>,
      IHumanActorOnlyCommand<CreateUserResult>
{
    public string RequiredPermission => "user.create";
}