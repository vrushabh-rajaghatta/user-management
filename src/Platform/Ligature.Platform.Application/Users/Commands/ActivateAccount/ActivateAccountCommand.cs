using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.ActivateAccount;

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
public sealed record ActivateAccountCommand(
    string TokenPlainText,
    string NewPassword)
    : IAnonymousCommand<ActivateAccountResult>;
