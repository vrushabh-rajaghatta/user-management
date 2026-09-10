using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Establishes the caller of a token-bearer command from the identity that
/// the token's consumption already authenticated (AUD-D28).
///
/// The other establishment path, ICallerEstablisher, turns a SessionId into
/// a caller before the pipeline runs. That cannot work here. A token-bearer
/// command carries its credential in its payload, and which identity the
/// token belongs to is not known until the conditional UPDATE that consumes
/// it has run — inside the command's own transaction. So this runs inside
/// the handler, after that UPDATE has succeeded and only then.
///
/// The identity is therefore an INPUT, not something this resolves. It must
/// be the exact UserIdentityId the consumption returned. Anything else — a
/// second token lookup, a walk from the user back to an identity, a fallback
/// to the session establisher — would reintroduce the drift this parameter
/// exists to prevent, and the actor on the resulting records would no longer
/// be provably the bearer that was authenticated.
///
/// It adds NO eligibility rules. Whether a deactivated user may still
/// activate an account is a User Management decision and is recorded as an
/// open requirement; deciding it here would turn an audit change into an
/// authentication-policy change.
/// </summary>
public interface ITokenBearerEstablisher
{
    /// <summary>
    /// Returns whether a caller was established. False when the identity or
    /// its user is missing, which after a successful consumption in the same
    /// transaction means the model has been violated rather than that the
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
