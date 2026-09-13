using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.ResetPassword;

/// <summary>
/// CRD-C3. The reset token IS the authorisation, as the activation token is
/// for CRD-C1: holding the emailed secret proves control of the mailbox, so no
/// caller and no permission are declared.
/// </summary>
/// <param name="TokenPlainText">
/// The delivered token, "{tokenId}.{secret}". Never persisted.
/// </param>
public sealed record ResetPasswordCommand(
    string TokenPlainText,
    string NewPassword)
    : IAnonymousCommand<ResetPasswordResult>;
