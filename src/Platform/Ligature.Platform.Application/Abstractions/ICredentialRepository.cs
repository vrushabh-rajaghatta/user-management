using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface ICredentialRepository
{
    Task AddAsync(Credential credential, CancellationToken cancellationToken);
}
