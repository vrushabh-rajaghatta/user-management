using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserIdentityRepository
{
    Task<bool> ExistsWithUsernameAsync(
        string username,
        CancellationToken cancellationToken);

    Task AddAsync(
        UserIdentity identity,
        CancellationToken cancellationToken);
}