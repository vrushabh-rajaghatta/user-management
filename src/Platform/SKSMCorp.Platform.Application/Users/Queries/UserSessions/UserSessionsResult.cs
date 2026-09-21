using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.UserSessions;

/// <summary>SES-Q1's response: the user's active sessions, most recently active first.</summary>
public sealed record UserSessionsResult(IReadOnlyList<UserSessionView> Sessions);

/// <summary>
/// One active session as served. IdleExpiresAt is /me's conservative instant
/// (last activity plus the effective idle timeout, no tolerance), never a
/// deadline. Current is true only for the caller's own session.
/// </summary>
public sealed record UserSessionView(
    UserSessionId SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt,
    string? IpAddress,
    string? UserAgent,
    bool Current);
