using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Services;

/// <summary>
/// AUT-Q7's catalogue lookup: the entry behind a code, whether or not it is
/// still active. Retired entries are returned, because retired is not unknown
/// (RW9).
/// </summary>
public sealed class PermissionCatalogueEntryReader : IPermissionCatalogueEntryReader
{
    private readonly SKSMCorpDbContext _dbContext;

    public PermissionCatalogueEntryReader(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<RequestedPermission?> FindAsync(
        string permissionCode,
        CancellationToken cancellationToken)
    {
        // NO IsActive FILTER, deliberately (RW9): retired is not unknown. The
        // entry comes back with its flag, and the caller reports it — which is
        // what lets a reader tell "retired, so nobody" from "nobody holds it".
        return await _dbContext.Set<Permission>()
            .AsNoTracking()
            .Where(x => x.Code == permissionCode)
            .Select(x => new RequestedPermission(x.Id.Value, x.Code, x.Name, x.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
