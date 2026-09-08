using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IPasswordHistoryRepository
{
    Task AddAsync(PasswordHistory history, CancellationToken cancellationToken);
}
