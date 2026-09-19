using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserSessions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.MySessions;

/// <summary>
/// SES-Q2 GetMySessions (docs/requirements.md, "SES-Q2 GetMySessions on the My
/// account page"): the caller's own active sessions.
///
/// SELF, so no permission: the user is the authenticated caller from the
/// execution context, and nothing in the request can name another. Everything
/// about the sessions themselves is ActiveSessionListing's — the same meaning
/// SES-Q1 serves an administrator. Not audited.
/// </summary>
public sealed class MySessionsQueryHandler : IQueryHandler<MySessionsQuery, MySessionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly ActiveSessionListing _listing;

    public MySessionsQueryHandler(IExecutionContext executionContext, IClock clock, ActiveSessionListing listing)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(listing);

        _executionContext = executionContext;
        _clock = clock;
        _listing = listing;
    }

    public async Task<MySessionsResult> Handle(MySessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        return new MySessionsResult(
            await _listing.ListAsync(
                _executionContext.UserId, query.CallerSessionId, _clock.UtcNow, cancellationToken));
    }
}
