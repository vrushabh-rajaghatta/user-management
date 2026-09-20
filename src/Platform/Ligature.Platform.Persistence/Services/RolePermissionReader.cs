using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Queries.RolePermissions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// AUT-Q3's read (docs/requirements.md, "Role administration read", RA5 and
/// RA11): the grants LIVE AT AN INSTANT — granted by then, and either never
/// revoked or revoked after it.
///
/// THE ROLE IS LOOKED UP FIRST, and null comes back when it does not exist.
/// That is what lets the route answer 404 while an existing role with nothing
/// granted answers an empty list (RA11); collapsing the two here would make
/// that distinction unrecoverable.
///
/// Codes are ordered under the "C" collation — byte order. A permission code is
/// an identifier, not prose, and byte order is the same on every server.
/// </summary>
public sealed class RolePermissionReader : IRolePermissionReader
{
    private const string Collation = "C";

    private readonly LigatureDbContext _dbContext;

    public RolePermissionReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RolePermissionGrant>?> ReadAsync(
        RoleId roleId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Set<Role>()
            .AsNoTracking()
            .AnyAsync(role => role.Id == roleId, cancellationToken);

        if (!exists)
            return null;

        return await _dbContext.Set<RolePermission>()
            .AsNoTracking()
            .Where(grant => grant.RoleId == roleId
                && grant.GrantedAt <= asOf
                && (grant.RevokedAt == null || asOf < grant.RevokedAt))
            .Join(
                _dbContext.Set<Permission>().AsNoTracking(),
                grant => grant.PermissionId,
                permission => permission.Id,
                (grant, permission) => new { grant, permission })
            .OrderBy(row => EF.Functions.Collate(row.permission.Code, Collation))
            .Select(row => new RolePermissionGrant(
                row.grant.Id.Value,
                row.permission.Id.Value,
                row.permission.Code,
                row.permission.Name,
                row.permission.Resource,
                row.permission.Action,
                row.permission.RequiresHumanActor,
                row.grant.GrantedAt,
                row.grant.RevokedAt))
            .ToListAsync(cancellationToken);
    }
}
