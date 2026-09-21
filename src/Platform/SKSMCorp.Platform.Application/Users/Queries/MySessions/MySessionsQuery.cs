using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.MySessions;

/// <summary>
/// SES-Q2 GetMySessions (docs/requirements.md, "SES-Q2 GetMySessions on the
/// My account page"): the caller's own active sessions. Self, so no permission
/// — the user is the caller, and nothing in the request names one.
///
/// <paramref name="CallerSessionId"/> is the session the Host recovered from
/// the carrier it verified, passed explicitly as MeQuery receives it. It
/// decides only which row is <c>current</c>.
/// </summary>
public sealed record MySessionsQuery(UserSessionId? CallerSessionId)
    : IQuery<MySessionsResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.NotRequired;
}
