using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Queries.RoleAssignments;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Services;

/// <summary>
/// AUT-Q2's stored facts: every assignment of one user, with its role's name
/// and the names of the administrators who granted and revoked it. It derives
/// no state and filters nothing by time; the handler does both, once.
///
/// AsNoTracking: nothing read here is written back.
/// </summary>
public sealed class UserRoleAssignmentReader : IUserRoleAssignmentReader
{
    private readonly SKSMCorpDbContext _dbContext;

    public UserRoleAssignmentReader(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserRoleAssignmentRecord>?> ReadAsync(
        UserId userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var exists = await _dbContext.Set<User>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == userId, cancellationToken);

        if (!exists)
            return null;

        var rows = await (
            from assignment in _dbContext.Set<UserRole>().AsNoTracking()
            join role in _dbContext.Set<Role>().AsNoTracking()
                on assignment.RoleId equals role.Id
            join granter in _dbContext.Set<User>().AsNoTracking()
                on assignment.AssignedBy equals granter.Id
            join revoker in _dbContext.Set<User>().AsNoTracking()
                on assignment.RevokedBy equals revoker.Id into revokers
            from revoker in revokers.DefaultIfEmpty()
            where assignment.UserId == userId
            select new
            {
                assignment.Id,
                assignment.RoleId,
                RoleName = role.Name,
                assignment.EffectiveFrom,
                assignment.EffectiveTo,
                assignment.AssignedAt,
                assignment.AssignedBy,
                GranterName = granter.DisplayName,
                assignment.AssignmentReason,
                assignment.RevokedAt,
                assignment.RevokedBy,
                RevokerName = revoker == null ? null : revoker.DisplayName,
                assignment.RevocationReason,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new UserRoleAssignmentRecord(
                x.Id,
                x.RoleId,
                x.RoleName,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.AssignedAt,
                new AssignmentActor(x.AssignedBy, x.GranterName),
                x.AssignmentReason,
                x.RevokedAt,
                x.RevokedBy is null ? null : new AssignmentActor(x.RevokedBy, x.RevokerName!),
                x.RevocationReason))
            .ToList();
    }
}
