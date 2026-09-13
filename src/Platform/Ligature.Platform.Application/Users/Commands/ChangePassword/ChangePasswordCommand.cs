using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.ChangePassword;

/// <summary>
/// CRD-C4. Authenticated and self-only: like SES-C2 it declares no catalogue
/// permission, because "self" is a relationship rather than a grant, and the
/// handler checks that relationship explicitly.
///
/// Human-only: the password belongs to the local identity that owns an
/// interactive session (inv. 26).
/// </summary>
/// <param name="CurrentSessionId">
/// The session this request presented, taken from the carrier and never from a
/// body. It names the identity whose password changes (D4) and the one session
/// that survives the change (A5).
/// </param>
public sealed record ChangePasswordCommand(
    UserSessionId CurrentSessionId,
    string CurrentPassword,
    string NewPassword)
    : IHumanActorOnlyCommand<ChangePasswordResult>;
