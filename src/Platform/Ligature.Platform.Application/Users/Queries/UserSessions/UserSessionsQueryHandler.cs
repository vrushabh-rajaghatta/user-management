using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserSessions;

/// <summary>SES-Q1 GetActiveSessions for one user.</summary>
public sealed class UserSessionsQueryHandler : IQueryHandler<UserSessionsQuery, UserSessionsResult>
{
    // RED-TEST STUB: reads nothing.
    public Task<UserSessionsResult> Handle(UserSessionsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new UserSessionsResult([]));
}
