using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly LigatureDbContext _dbContext;

    public RolePermissionRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// AUT-C7's pre-check (RP2). "Live" is exactly the partial unique index's
    /// predicate — revoked_at IS NULL — so the check and the guarantee ask the
    /// same question. Read-only: the caller only wants to know.
    /// </summary>
    public async Task<RolePermission?> FindLiveAsync(
        RoleId roleId, PermissionId permissionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);
        ArgumentNullException.ThrowIfNull(permissionId);

        return await _dbContext.Set<RolePermission>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RoleId == roleId && x.PermissionId == permissionId && x.RevokedAt == null,
                cancellationToken);
    }

    /// <summary>AUT-C8: tracked, because this one is about to be closed.</summary>
    public async Task<RolePermission?> FindTrackedAsync(
        RolePermissionId rolePermissionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rolePermissionId);

        return await _dbContext.Set<RolePermission>()
            .FirstOrDefaultAsync(x => x.Id == rolePermissionId, cancellationToken);
    }

    /// <summary>AUT-C7 adds a NEW row; the unit of work owns the save.</summary>
    public Task AddAsync(RolePermission grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);

        _dbContext.Add(grant);

        return Task.CompletedTask;
    }
}
