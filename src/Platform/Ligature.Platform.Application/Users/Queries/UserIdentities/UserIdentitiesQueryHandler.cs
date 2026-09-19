using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserIdentities;

/// <summary>COMPILE-ONLY STUB (red tests).</summary>
public sealed class UserIdentitiesQueryHandler : IQueryHandler<UserIdentitiesQuery, UserIdentitiesResult>
{
    public Task<UserIdentitiesResult> Handle(UserIdentitiesQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
