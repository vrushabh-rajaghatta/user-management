using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Queries.RoleMembers;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// AUT-Q4's stored facts: every holding of one role, with the holder's own
/// values and the name of the administrator who granted it. It derives no
/// state and filters nothing by time; the handler does both, once.
///
/// AsNoTracking: nothing read here is written back.
/// </summary>
public sealed class RoleMemberReader : IRoleMemberReader
{
    private readonly LigatureDbContext _dbContext;

    public RoleMemberReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<RoleMemberRecord>?> ReadAsync(
        RoleId roleId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q4 is not implemented yet.");
}
