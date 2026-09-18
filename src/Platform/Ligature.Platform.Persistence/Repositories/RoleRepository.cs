using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly LigatureDbContext _dbContext;

    public RoleRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>Read-only: no command that uses it changes the role.</summary>
    public async Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);

        return await _dbContext.Set<Role>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    }
}
