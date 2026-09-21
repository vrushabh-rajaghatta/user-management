using SKSMCorp.Platform.Application.Users.Queries.UserSessions;

namespace SKSMCorp.Platform.Application.Users.Queries.MySessions;

/// <summary>SES-Q2's response: SES-Q1's shape, for the caller's own user.</summary>
public sealed record MySessionsResult(IReadOnlyList<UserSessionView> Sessions);
