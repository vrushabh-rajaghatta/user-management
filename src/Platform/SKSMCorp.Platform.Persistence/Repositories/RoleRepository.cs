using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly SKSMCorpDbContext _dbContext;

    public RoleRepository(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>Read-only: the commands that use it do not change the role.</summary>
    public async Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);

        return await _dbContext.Set<Role>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    }

    /// <summary>AUT-C4: tracked, because this one is about to change. No lock (RM5).</summary>
    public async Task<Role?> FindTrackedAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);

        return await _dbContext.Set<Role>()
            .FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    }

    /// <summary>
    /// AUT-C3 (RC1). PostgreSQL's lower() rather than .NET's fold, as the
    /// username and email pre-checks use, so the comparison the command makes
    /// is the database's own: a pre-check that folded differently would refuse
    /// codes the index accepts, or accept ones it refuses.
    /// </summary>
    public async Task<bool> ExistsWithCodeAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return await _dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1 FROM "role" WHERE lower("code") = lower({code})
                ) AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Adds to the change tracker only. UnitOfWork owns the save.</summary>
    public Task AddAsync(Role role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);

        _dbContext.Add(role);

        return Task.CompletedTask;
    }
}
