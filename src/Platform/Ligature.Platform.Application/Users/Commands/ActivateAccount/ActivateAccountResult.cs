using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.ActivateAccount;

/// <summary>
/// Carries no password, no token and no credential material — nothing the
/// caller supplied and nothing that would be dangerous to return.
/// </summary>
public sealed record ActivateAccountResult(
    UserIdentityId UserIdentityId);
