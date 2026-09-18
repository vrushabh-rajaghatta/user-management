using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserProfile;

/// <summary>USR-Q1 v1. Stub — not yet implemented.</summary>
public sealed class UserProfileQueryHandler : IQueryHandler<UserProfileQuery, UserProfileResult>
{
    public UserProfileQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserProfileReader reader)
    {
    }

    public Task<UserProfileResult> Handle(UserProfileQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
