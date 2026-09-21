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

    public Task<RequestedPermission?> FindAsync(
        string permissionCode,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q7 is not implemented yet.");
}
