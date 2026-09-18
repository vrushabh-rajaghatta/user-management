using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class UserRoleRepository : IUserRoleRepository
{
    private readonly LigatureDbContext _dbContext;

    public UserRoleRepository(LigatureDbContext dbContext)
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
