using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.SignOutEverywhere;

/// <summary>
/// SES-C4, self form — a command identity of its own (D1).
///
/// Authenticated and self-only: it acts on the caller's own user and declares
/// no catalogue permission. Human-only, because it operates on the caller's
/// interactive sessions (inv. 26).
/// </summary>
/// <param name="CurrentSessionId">
/// The session presenting the request, from the carrier and never from a body.
/// Consulted only when <paramref name="KeepCurrentSession"/> is true.
/// </param>
/// <param name="KeepCurrentSession">
/// False by default: signing out everywhere includes this session unless the
/// caller explicitly excludes it (D4).
/// </param>
/// <param name="Reason">
/// OPTIONAL — a change from the catalogue's required input, recorded as change
/// control. When absent or blank the command records a fixed explanation.
/// </param>
public sealed record SignOutEverywhereCommand(
    UserSessionId? CurrentSessionId,
    bool KeepCurrentSession,
    string? Reason)
    : IHumanActorOnlyCommand<SignOutEverywhereResult>;
