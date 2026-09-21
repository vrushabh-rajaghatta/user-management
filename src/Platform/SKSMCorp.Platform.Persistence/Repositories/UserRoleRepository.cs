using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Repositories;

public sealed class UserRoleRepository : IUserRoleRepository
{
    /// <summary>
    /// AUT-C7's half of the two-edge check (RP6). ACTIVE is the frozen
    /// wording, implemented literally (RG3): not revoked, and within the
    /// half-open period at the instant asked about — the same shape RA2's
    /// holder count and the authorisation predicate use, with the actor type
    /// added.
    ///
    /// A FUTURE agent assignment therefore does not count. That is a recorded
    /// gap in RP6 itself, not an oversight here, and it is not quietly widened.
    /// </summary>
    public async Task<bool> HasActiveAgentAssignmentAsync(
        RoleId roleId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);

        return await _dbContext.Set<UserRole>()
            .AsNoTracking()
            .AnyAsync(
                x => x.RoleId == roleId
                    && x.ActorType == ActorType.Agent
                    && x.RevokedAt == null
                    && x.EffectiveFrom <= at
                    && (x.EffectiveTo == null || at < x.EffectiveTo),
                cancellationToken);
    }

    private readonly SKSMCorpDbContext _dbContext;

    public UserRoleRepository(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save, which is
    /// where the no-overlap exclusion constraints (UR5/UR6) are checked, and
    /// where a violation is translated.
    /// </summary>
    public Task AddAsync(UserRole assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        _dbContext.Add(assignment);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<UserRole?> FindAsync(UserRoleId assignmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignmentId);

        return await _dbContext.Set<UserRole>()
            .FirstOrDefaultAsync(x => x.Id == assignmentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> FindForUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await _dbContext.Set<UserRole>()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
