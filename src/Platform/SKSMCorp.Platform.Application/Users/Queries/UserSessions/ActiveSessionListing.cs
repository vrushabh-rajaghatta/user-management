using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.UserSessions;

/// <summary>
/// The ONE implementation of what an active-session list means, shared by
/// SES-Q1 (an administrator reading any user) and SES-Q2 (the caller reading
/// their own) — docs/requirements.md, "SES-Q2 GetMySessions on the My account
/// page", MY2. The two handlers decide only whose sessions and whether the
/// caller may see them; everything about the sessions themselves is here:
///
/// <list type="bullet">
/// <item>the active test — the canonical one the per-request check and SES-C3
/// apply, through <see cref="IUserSessionRepository.FindActiveForUserAsync"/>,
/// across every identity of the user;</item>
/// <item>the order — most recently active first, then session id;</item>
/// <item><c>IdleExpiresAt</c> — /me's conservative instant: last activity plus
/// the effective idle timeout, without the enforcement tolerance;</item>
/// <item><c>Current</c> — SESSION ids: the one row whose id is the session the
/// Host recovered from the request's carrier;</item>
/// <item>the projection to the eight fields.</item>
/// </list>
///
/// Application-level, deliberately: not an HTTP or web abstraction. The two
/// endpoints and the two pages share the server's meaning, not code.
/// </summary>
public sealed class ActiveSessionListing
{
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;

    public ActiveSessionListing(
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver)
    {
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);

        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
    }

    public async Task<IReadOnlyList<UserSessionView>> ListAsync(
        UserId userId, UserSessionId? callerSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, cancellationToken);

        var sessions = await _userSessionRepository.FindActiveForUserAsync(
            userId, now, policy.SessionIdleTimeout, cancellationToken);

        return sessions
            .OrderByDescending(x => x.LastActivityAt)
            .ThenBy(x => x.Id.Value)
            .Select(x => new UserSessionView(
                x.Id,
                x.CreatedAt,
                x.LastActivityAt,
                x.ExpiresAt,
                x.LastActivityAt + policy.SessionIdleTimeout,
                x.IpAddress?.ToString(),
                x.UserAgent,
                callerSessionId is { } caller && x.Id == caller))
            .ToList();
    }
}
