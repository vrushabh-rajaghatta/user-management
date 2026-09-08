using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class CredentialRepository : ICredentialRepository
{
    private readonly LigatureDbContext _dbContext;

    public CredentialRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save, so a
    /// credential cannot be committed without its password-history row (CR5).
    /// </summary>
    public Task AddAsync(Credential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        _dbContext.Add(credential);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Credential?> FindByIdentityAsync(
        UserIdentityId userIdentityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIdentityId);

        // Tracked deliberately: SES-C1 increments the attempt counter, locks,
        // unlocks and rehashes through this instance, and UnitOfWork commits
        // those writes with the rest of the operation.
        return await _dbContext.Set<Credential>()
            .FirstOrDefaultAsync(
                x => x.UserIdentityId == userIdentityId, cancellationToken);
    }
}
