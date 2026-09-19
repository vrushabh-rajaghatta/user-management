using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.UserSessions;

/// <summary>
/// The one implementation of what an active-session list means, shared by
/// SES-Q1 and SES-Q2 (docs/requirements.md, "SES-Q2 GetMySessions on the My
/// account page", MY2).
/// </summary>
public sealed class ActiveSessionListing
{
    // RED-TEST STUB: lists nothing.
    public Task<IReadOnlyList<UserSessionView>> ListAsync(
        UserId userId, UserSessionId? callerSessionId, DateTimeOffset now, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<UserSessionView>>([]);
}
