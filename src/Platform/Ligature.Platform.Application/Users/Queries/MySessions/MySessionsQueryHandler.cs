using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.MySessions;

/// <summary>SES-Q2 GetMySessions.</summary>
public sealed class MySessionsQueryHandler : IQueryHandler<MySessionsQuery, MySessionsResult>
{
    // RED-TEST STUB: reads nothing.
    public Task<MySessionsResult> Handle(MySessionsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new MySessionsResult([]));
}
