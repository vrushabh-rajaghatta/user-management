using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Queries.RoleMembers;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

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
    private const string Collation = "unicode";

    private readonly LigatureDbContext _dbContext;

    public RoleMemberReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RoleMemberRecord>?> ReadAsync(
        RoleId roleId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);

        // The role is looked up FIRST, so "this role has no holders" and "there
        // is no such role" stay different answers (RH8). An empty list from a
        // role that does not exist would be a lie the route could not detect.
        var exists = await _dbContext.Set<Role>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == roleId, cancellationToken);

        if (!exists)
            return null;

        // NO SCOPE PREDICATE, deliberately (RH3). Every assignment is global —
        // GrantRole writes nothing else — and adding one here would introduce
        // scope semantics this story does not have. It also keeps the rows
        // consistent with AUT-Q5's holder count, which has no scope condition.
        //
        // The order is applied HERE, in SQL, so the ICU collation decides it:
        // the handler filters this list and Where preserves order.
        var rows = await (
            from assignment in _dbContext.Set<UserRole>().AsNoTracking()
            join holder in _dbContext.Set<User>().AsNoTracking()
                on assignment.UserId equals holder.Id
            join granter in _dbContext.Set<User>().AsNoTracking()
                on assignment.AssignedBy equals granter.Id
            where assignment.RoleId == roleId
            orderby EF.Functions.Collate(holder.DisplayName, Collation), assignment.Id
            select new
            {
                assignment.Id,
                assignment.UserId,
                holder.DisplayName,
                holder.Email,
                holder.Status,
                assignment.EffectiveFrom,
                assignment.EffectiveTo,
                assignment.AssignedAt,
                assignment.AssignedBy,
                GranterName = granter.DisplayName,
                assignment.AssignmentReason,
                assignment.RevokedAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new RoleMemberRecord(
                x.Id,
                x.UserId,
                x.DisplayName,
                x.Email?.Value,
                x.Status,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.AssignedAt,
                new RoleMemberActor(x.AssignedBy, x.GranterName),
                x.AssignmentReason,
                x.RevokedAt))
            .ToList();
    }
}
