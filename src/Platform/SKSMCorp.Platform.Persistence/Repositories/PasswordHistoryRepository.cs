using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Repositories;

/// <summary>
/// password_history is insert-only (PH3): the application role is granted
/// neither UPDATE nor DELETE, because a table whose only purpose is preventing
/// reuse is worthless if its entries can quietly disappear.
/// </summary>
public sealed class PasswordHistoryRepository : IPasswordHistoryRepository
{
    private readonly SKSMCorpDbContext _dbContext;

    public PasswordHistoryRepository(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task AddAsync(PasswordHistory history, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        _dbContext.Add(history);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasswordHistory>> FindRecentAsync(
        UserIdentityId userIdentityId,
        int depth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIdentityId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        // Newest first, then capped. The cap is the policy's depth, which is
        // a FLOOR — max(tenant, baseline) — so a caller never asks for fewer
        // rows than the baseline requires.
        return await _dbContext.Set<PasswordHistory>()
            .AsNoTracking()
            .Where(x => x.UserIdentityId == userIdentityId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(depth)
            .ToListAsync(cancellationToken);
    }
}
