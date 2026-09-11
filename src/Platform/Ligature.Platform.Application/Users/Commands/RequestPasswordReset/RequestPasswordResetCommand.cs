using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.RequestPasswordReset;

/// <summary>
/// CRD-C2. Anonymous and unauthenticated: somebody who cannot sign in is
/// asking for a way back, so requiring a caller would be circular.
///
/// The requester is not the subject, and cannot be assumed to be. Anyone may
/// type anyone else's address into the form, which is why the issued token is
/// attributed to the System actor rather than to the account it belongs to —
/// recording the owner as the issuer would assert something the system does
/// not know (UM §6.5).
/// </summary>
/// <param name="EmailOrUsername">
/// Whatever the requester typed. Treated as BOTH keys: usernames are
/// unconstrained labels and may look exactly like an address, so neither can
/// be assumed from the shape of the string.
/// </param>
/// <param name="IpAddress">
/// Forensic context for the audit record, declared as PII by the catalogue
/// (Payload.RequestIp). Optional: an unknown origin is recorded as absent
/// rather than invented.
///
/// It is NOT used for rate limiting, which does not exist yet — see the
/// blocking dependency recorded against this command in
/// docs/requirements.md.
/// </param>
public sealed record RequestPasswordResetCommand(
    string EmailOrUsername,
    string? IpAddress)
    : IAnonymousCommand<RequestPasswordResetResult>;
