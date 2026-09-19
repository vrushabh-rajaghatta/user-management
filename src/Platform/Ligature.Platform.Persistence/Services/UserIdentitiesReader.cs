using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserIdentities;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Persistence.Services;

/// <summary>COMPILE-ONLY STUB (red tests).</summary>
public sealed class UserIdentitiesReader : IUserIdentitiesReader
{
    public Task<IReadOnlyList<UserIdentityView>?> ReadAsync(
        UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
