using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.RateLimiting;

namespace Ligature.Platform.Application.Users.Commands.ResetPassword;

/// <summary>
/// CRD-C3. The reset token IS the authorisation, as the activation token is
/// for CRD-C1: holding the emailed secret proves control of the mailbox, so no
/// caller and no permission are declared.
/// </summary>
/// <param name="TokenPlainText">
/// The delivered token, "{tokenId}.{secret}". Never persisted.
/// </param>
/// <param name="IpAddress">
/// The client address the Host resolved, for rate limiting (behaviour 11)
/// only. Recorded nowhere. Null when no address was resolvable.
/// </param>
public sealed record ResetPasswordCommand(
    string TokenPlainText,
    string NewPassword,
    string? IpAddress = null)
    : IBearerAuthenticatedCommand<ResetPasswordResult>, IRateLimitedCommand
{
    /// <summary>
    /// Behaviour 11: per client address only. The token is the authorization
    /// boundary; the limit bounds the cost of the derivations it buys.
    /// </summary>
    IReadOnlyList<RateLimitSubject> IRateLimitedCommand.RateLimitSubjects =>
        [new(RateLimitRules.ResetPasswordByClientAddress, IpAddress)];
}
