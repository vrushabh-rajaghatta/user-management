using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.GrantableRoles;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// The active roles the grant form offers. Deliberately narrow — not AUT-Q5:
/// no counts, no includeInactive, no agent-assignable filter. Ordered by name
/// under ICU "unicode" (PostgreSQL's root collation), then id, as USR-Q1 orders
/// users, so the order is the same whatever the database's default collation.
/// </summary>
public sealed class GrantableRoleReader : IGrantableRoleReader
{
    private const string Collation = "unicode";

    private readonly LigatureDbContext _dbContext;

    public GrantableRoleReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<GrantableRole>> ReadAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Set<Role>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => EF.Functions.Collate(x.Name, Collation))
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Description })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new GrantableRole(x.Id, x.Name, x.Description)).ToList();
    }
}
