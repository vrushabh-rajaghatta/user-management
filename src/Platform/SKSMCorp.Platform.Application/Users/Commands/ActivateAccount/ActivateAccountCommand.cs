using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.RateLimiting;

namespace SKSMCorp.Platform.Application.Users.Commands.ActivateAccount;

/// <summary>
/// CRD-C1. The token IS the authorisation, so this command declares no
/// permission and no caller: whoever holds the emailed secret proves control of
/// the mailbox, which is the whole point of the mechanism. An administrator
/// setting the password instead would know it, and every document that user
/// later approved could be argued to have been signed by someone else.
/// </summary>
/// <param name="TokenPlainText">
/// The delivered token, "{tokenId}.{secret}". Never persisted.
/// </param>
/// <param name="IpAddress">
/// The client address the Host resolved, for rate limiting (behaviour 11)
/// only. Recorded nowhere. Null when no address was resolvable.
/// </param>
public sealed record ActivateAccountCommand(
    string TokenPlainText,
    string NewPassword,
    string? IpAddress = null)
    : IBearerAuthenticatedCommand<ActivateAccountResult>, IRateLimitedCommand
{
    /// <summary>
    /// Behaviour 11: per client address only. The token is the authorization
    /// boundary; the limit bounds the cost of the derivations it buys.
    /// </summary>
    IReadOnlyList<RateLimitSubject> IRateLimitedCommand.RateLimitSubjects =>
        [new(RateLimitRules.ActivateAccountByClientAddress, IpAddress)];
}
