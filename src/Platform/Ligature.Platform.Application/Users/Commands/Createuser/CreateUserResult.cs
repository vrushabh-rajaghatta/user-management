using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.CreateUser;

public sealed record CreateUserResult(
    UserId UserId,
    UserIdentityId UserIdentityId);