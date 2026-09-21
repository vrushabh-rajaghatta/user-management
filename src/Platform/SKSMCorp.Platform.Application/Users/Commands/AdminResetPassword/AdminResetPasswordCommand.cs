using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.AdminResetPassword;

/// <summary>
/// CRD-C5. An administrator issues a password reset link to a user.
///
/// There is no password input, and there must never be one: the administrator
/// cannot set the value and never receives it. Issuing a token delivered to the
/// user's own mailbox is the only permitted mechanism, because an administrator
/// who knew a user's password would make every approval that user later gave
/// contestable.
/// </summary>
/// <param name="Reason">
/// Required. AdminPasswordResetIssued requires a reason, and it is a human
/// explanation from the administrator, never a code (AUD-7).
/// </param>
public sealed record AdminResetPasswordCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<AdminResetPasswordResult>,
      IHumanActorOnlyCommand<AdminResetPasswordResult>
{
    public string RequiredPermission => "user.resetpassword";
}
