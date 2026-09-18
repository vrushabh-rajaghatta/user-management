using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.Me;

/// <summary>
/// Who is the caller for this request? (B6-B.)
///
/// The SessionId arrives from the carrier the Host already verified, exactly as
/// SignOutCommand takes it — a client cannot name someone else's session. It is
/// the KEY FOR OBTAINING the session's timing, and not an invitation to decide
/// again whether that session is valid: caller establishment is the
/// authentication boundary (D4). A verified signature is not an established
/// caller, so the handler reads the boundary's decision before using it.
///
/// NotRequired, and deliberately so: /me answers about the caller and refuses
/// nobody who has been established. It declares that rather than being silent
/// about it, because under docs/architecture.md section 11 there is no such
/// thing as an accidentally unauthorised query. Nothing about its behaviour
/// changed when this declaration was added.
/// </summary>
public sealed record MeQuery(UserSessionId SessionId)
    : IQuery<MeResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.NotRequired;
}
