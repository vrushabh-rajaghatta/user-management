using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.RevokeUserSessions;

/// <summary>
/// SES-C4, administrator form — a command identity of its own.
///
/// SES-C4's frozen authorisation is "self / session.revoke", and a command
/// carries one fixed permission under pipeline behaviour 3: authorisation is
/// never re-decided inside a handler. So the capability is two commands — this
/// one, and SignOutEverywhere for the self path (D1; a catalogue amendment
/// recorded in docs/requirements.md).
///
/// Not human-only, following the session.revoke seed (D3).
/// </summary>
/// <param name="Reason">
/// Required. The administrator's explanation, recorded as the audit Reason on
/// every SessionRevoked; the sessions themselves carry the code AdminRevoked.
/// </param>
public sealed record RevokeUserSessionsCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<RevokeUserSessionsResult>
{
    public string RequiredPermission => "session.revoke";
}
