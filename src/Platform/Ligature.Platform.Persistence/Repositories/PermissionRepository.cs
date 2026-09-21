using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

/// <summary>
/// The three facts AUT-C7 decides on, and nothing else (RG7). The permission
/// catalogue is release-owned (PE2), so there is no write path here.
/// </summary>
public sealed class PermissionRepository : IPermissionRepository
{
    private readonly LigatureDbContext _dbContext;

    public PermissionRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<PermissionFacts?> FindAsync(
        PermissionId permissionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissionId);

        return await _dbContext.Set<Permission>()
            .AsNoTracking()
            .Where(x => x.Id == permissionId)
            .Select(x => new PermissionFacts(x.Id, x.Code, x.IsActive, x.RequiresHumanActor))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
