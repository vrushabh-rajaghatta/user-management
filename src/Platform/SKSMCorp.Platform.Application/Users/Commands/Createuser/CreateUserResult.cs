using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.CreateUser;

public sealed record CreateUserResult(
    UserId UserId,
    UserIdentityId UserIdentityId);