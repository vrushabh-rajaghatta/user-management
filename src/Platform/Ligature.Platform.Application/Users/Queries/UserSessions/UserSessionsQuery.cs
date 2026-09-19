using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserSessions;

/// <summary>
/// SES-Q1 GetActiveSessions, for one user (docs/requirements.md, "SES-Q1
/// GetActiveSessions and Revoke on the User detail page"), under session.read.
///
/// <paramref name="CallerSessionId"/> is the session the Host recovered from
/// the carrier it verified — passed explicitly, as MeQuery and SES-C2 receive
/// it, never from the execution context and never from the client. It decides
/// only which row is <c>current</c>.
/// </summary>
public sealed record UserSessionsQuery(UserId UserId, UserSessionId? CallerSessionId)
    : IQuery<UserSessionsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("session.read");
}
