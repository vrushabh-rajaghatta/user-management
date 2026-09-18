using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>Reads roles for the commands that act on them (AUT-C1).</summary>
public interface IRoleRepository
{
    Task<Role?> FindAsync(
        RoleId roleId,
        CancellationToken cancellationToken);
}
