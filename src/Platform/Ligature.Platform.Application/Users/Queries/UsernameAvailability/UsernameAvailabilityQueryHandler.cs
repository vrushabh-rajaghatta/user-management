using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UsernameAvailability;

/// <summary>IDN-Q3 CheckUsernameAvailable.</summary>
public sealed class UsernameAvailabilityQueryHandler
    : IQueryHandler<UsernameAvailabilityQuery, UsernameAvailabilityResult>
{
    // RED-TEST STUB: everything is available.
    public Task<UsernameAvailabilityResult> Handle(UsernameAvailabilityQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new UsernameAvailabilityResult(true));
}
