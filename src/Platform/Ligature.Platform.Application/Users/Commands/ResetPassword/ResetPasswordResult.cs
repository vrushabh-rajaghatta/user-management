using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.ResetPassword;

/// <summary>
/// Carries no password, no token and no credential material — the same shape
/// as ActivateAccountResult, and for the same reason. Returned only after the
/// bearer has proven possession of a live token, so the identity it names is
/// the bearer's own.
/// </summary>
public sealed record ResetPasswordResult(
    UserIdentityId UserIdentityId);
