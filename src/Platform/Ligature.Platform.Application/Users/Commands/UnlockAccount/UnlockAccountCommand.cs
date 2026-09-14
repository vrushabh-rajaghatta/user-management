using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.UnlockAccount;

/// <summary>
/// CRD-C6. An administrator clears a live lockout on one identity.
///
/// Keyed by the IDENTITY, not the user, because lockout state lives on the
/// credential of a local identity (UM §6.3). Resolving through the user would
/// have to choose between identities, and choosing is how a command reaches
/// one it was never asked about.
/// </summary>
/// <param name="Reason">
/// Required. AccountUnlocked requires a reason — "unlocking without recording a
/// reason loses the security narrative" (catalogue).
/// </param>
public sealed record UnlockAccountCommand(
    UserIdentityId UserIdentityId,
    string Reason)
    : IAuthorizableCommand<UnlockAccountResult>,
      IHumanActorOnlyCommand<UnlockAccountResult>
{
    public string RequiredPermission => "user.unlock";
}
