using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Roles.Queries.PermissionCatalogue;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Services;

/// <summary>
/// AUT-Q6's read (docs/requirements.md, "Role administration read"): the
/// catalogue as it is, retired entries included, so a retired one is visibly
/// retired rather than missing. It is release-owned (PE2); nothing here writes.
///
/// Codes are ordered under "C", byte order, as AUT-Q3's are.
/// </summary>
public sealed class PermissionCatalogueReader : IPermissionCatalogueReader
{
    private const string Collation = "C";

    private readonly SKSMCorpDbContext _dbContext;

    public PermissionCatalogueReader(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PermissionCatalogueEntry>> ReadAsync(
        string? resource,
        bool? requiresHumanActor,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Set<Permission>()
            .AsNoTracking()
            .Where(permission => resource == null || permission.Resource == resource)
            .Where(permission => requiresHumanActor == null || permission.RequiresHumanActor == requiresHumanActor)
            .OrderBy(permission => EF.Functions.Collate(permission.Code, Collation))
            .Select(permission => new PermissionCatalogueEntry(
                permission.Id.Value,
                permission.Code,
                permission.Name,
                permission.Resource,
                permission.Action,
                permission.RequiresHumanActor,
                permission.IsActive))
            .ToListAsync(cancellationToken);
    }
}
