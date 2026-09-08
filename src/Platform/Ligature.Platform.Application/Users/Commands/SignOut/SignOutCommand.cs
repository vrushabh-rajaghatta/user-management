using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.SignOut;

/// <summary>
/// SES-C2. Authenticated, and deliberately NOT anonymous: it declares no
/// catalogue permission because its permission is "self", which is a
/// relationship rather than a grant.
///
/// That is exactly why IAnonymousCommand is an opt-in marker rather than
/// something inferred from the absence of IAuthorizableCommand — under the
/// inferred design this command would have silently become anonymous, and
/// anyone could have signed out anyone.
///
/// Human-only: sessions exist for interactive human authentication (inv. 26),
/// so a machine actor has none to end.
/// </summary>
/// <param name="SessionId">
/// Supplied by the caller. It is NOT read from the execution context, which
/// carries the authenticated actor rather than authentication transport state —
/// coupling it to a session would tie the application layer to the access-token
/// architecture deferred under section 17. Ownership is therefore checked
/// explicitly by the handler.
/// </param>
public sealed record SignOutCommand(UserSessionId SessionId)
    : IHumanActorOnlyCommand<SignOutResult>;
