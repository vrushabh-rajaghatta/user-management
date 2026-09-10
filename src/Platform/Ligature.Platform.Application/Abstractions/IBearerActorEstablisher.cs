using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Establishes the caller of a bearer-authenticated command from the identity
/// that the command's own authentication step already resolved (AUD-D28).
///
/// BEARER, not token. The frozen rule covers any command that carries its
/// credential in its payload: an activation token, a password at sign-in, and
/// whatever is deliberately added later. The mechanism differs; the shape does
/// not. Which identity the credential belongs to is unknowable until the step
/// that validates it has run, inside the command's own transaction, so this
/// runs inside the handler afterwards and only then.
///
/// The other establishment path, ICallerEstablisher, turns a SessionId into a
/// caller before the pipeline runs. That cannot work here, because there is no
/// session yet — at sign-in, creating one is the point.
///
/// The identity is therefore an INPUT, not something this resolves. It must be
/// the exact UserIdentityId that authentication returned: the row the token
/// consumption updated, or the identity whose password verified. Anything else
/// — a second lookup by username, a walk from the user back to an identity, a
/// fallback to the session establisher — would reintroduce the drift this
/// parameter exists to prevent, and the actor on the resulting records would
/// no longer be provably the bearer who authenticated.
///
/// It adds NO eligibility rules. Whether a deactivated user may still activate
/// an account is a User Management decision and is recorded as an open
/// requirement; deciding it here would turn an audit change into an
/// authentication-policy change.
/// </summary>
public interface IBearerActorEstablisher
{
    /// <summary>
    /// Returns whether a caller was established. False when the identity or
    /// its user is missing, which after a successful authentication in the
    /// same transaction means the model has been violated rather than that the
    /// caller did anything wrong.
    ///
    /// Returns rather than throws, for the same reason the session
    /// establisher does: the caller decides what a failure means, and here
    /// that decision is to answer with the same opaque rejection every other
    /// invalid activation gets.
    /// </summary>
    Task<bool> EstablishAsync(
        UserIdentityId identityId,
        CancellationToken cancellationToken);
}
