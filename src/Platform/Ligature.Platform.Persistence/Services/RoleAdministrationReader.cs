using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// AUT-Q5's read (docs/requirements.md, "Role administration read", RA1-RA4),
/// as one statement.
///
/// THE THREE DERIVED VALUES, each by its own rule:
///
///   permissionCount     live grants, whatever the catalogue says about the
///                       permission itself (RA3)
///   agentAssignable     no LIVE grant of a human-only permission (RA4), so a
///                       role with nothing granted is agent-assignable
///   activeHolderCount   DISTINCT holders whose assignment is Active at the
///                       instant the handler read (RA2) — assignment state,
///                       never the holder's user status
///
/// includeInactive and agentAssignableOnly narrow the rows and nothing else
/// (RA1): both are applied after the values above are computed.
///
/// The name order is explicit ICU "unicode", as the user list and the
/// grantable-role list are, so it does not depend on the database's default
/// collation.
/// </summary>
public sealed class RoleAdministrationReader : IRoleAdministrationReader
{
    private const string Collation = "unicode";

    private readonly LigatureDbContext _dbContext;

    public RoleAdministrationReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RoleSummary>> ReadAsync(
        bool includeInactive,
        bool agentAssignableOnly,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var grants = _dbContext.Set<RolePermission>().AsNoTracking();
        var permissions = _dbContext.Set<Permission>().AsNoTracking();
        var assignments = _dbContext.Set<UserRole>().AsNoTracking();

        var rows = _dbContext.Set<Role>()
            .AsNoTracking()
            .Where(role => includeInactive || role.IsActive)
            .Select(role => new
            {
                role.Id,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystemRole,
                role.IsActive,
                PermissionCount = grants.Count(grant => grant.RoleId == role.Id && grant.RevokedAt == null),
                HoldsHumanOnly = grants
                    .Where(grant => grant.RoleId == role.Id && grant.RevokedAt == null)
                    .Join(permissions, grant => grant.PermissionId, permission => permission.Id, (_, permission) => permission)
                    .Any(permission => permission.RequiresHumanActor),
                ActiveHolderCount = assignments
                    .Where(assignment => assignment.RoleId == role.Id
                        && assignment.RevokedAt == null
                        && assignment.EffectiveFrom <= at
                        && (assignment.EffectiveTo == null || at < assignment.EffectiveTo))
                    .Select(assignment => assignment.UserId)
                    .Distinct()
                    .Count(),
            })
            .Where(row => !agentAssignableOnly || !row.HoldsHumanOnly)
            .OrderBy(row => EF.Functions.Collate(row.Name, Collation))
            .ThenBy(row => row.Id);

        return await rows
            .Select(row => new RoleSummary(
                row.Id.Value,
                row.Code,
                row.Name,
                row.Description,
                row.IsSystemRole,
                row.IsActive,
                !row.HoldsHumanOnly,
                row.PermissionCount,
                row.ActiveHolderCount))
            .ToListAsync(cancellationToken);
    }
}
