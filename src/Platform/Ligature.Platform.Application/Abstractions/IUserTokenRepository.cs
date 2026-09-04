using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserTokenRepository
{
    Task AddAsync(
        UserToken token,
        CancellationToken cancellationToken);
}