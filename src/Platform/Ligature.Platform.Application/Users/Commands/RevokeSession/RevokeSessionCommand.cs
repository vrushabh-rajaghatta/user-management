using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.RevokeSession;

/// <summary>
/// SES-C3. An administrator ends one session.
///
/// Not human-only: session.revoke is seeded with RequiresHumanActor = false,
/// and this command does not silently strengthen the permission (D3).
/// </summary>
/// <param name="Reason">
/// Required. The administrator's explanation — the audit record's Reason. It is
/// never written to user_session.RevocationReason, which carries the controlled
/// code AdminRevoked (D2).
/// </param>
public sealed record RevokeSessionCommand(
    UserSessionId SessionId,
    string Reason)
    : IAuthorizableCommand<RevokeSessionResult>
{
    public string RequiredPermission => "session.revoke";
}
