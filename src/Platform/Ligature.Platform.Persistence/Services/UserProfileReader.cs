using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserProfile;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Persistence.Services;

/// <summary>USR-Q1 v1. Stub — not yet implemented.</summary>
public sealed class UserProfileReader : IUserProfileReader
{
    public Task<UserProfileResult?> ReadAsync(UserId userId, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
