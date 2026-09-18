using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.GrantableRoles;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Services;

/// <summary>RED STUB, and not registered.</summary>
public sealed class GrantableRoleReader : IGrantableRoleReader
{
    public GrantableRoleReader(LigatureDbContext dbContext)
    {
    }

    public Task<IReadOnlyList<GrantableRole>> ReadAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("The grantable-role list is not implemented.");
}
