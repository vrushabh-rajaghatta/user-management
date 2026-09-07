using Ligature.Platform.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ligature.Platform.Persistence.Database;

/// <summary>
/// One command = one transaction.
///
/// Repositories only add to the change tracker; this is the only place that
/// calls SaveChanges. That is what makes the deactivation cascade (USR-C4)
/// atomic by construction rather than by developer discipline: a handler cannot
/// accidentally half-commit, because it has no way to commit at all.
///
/// Isolation stays at Read Committed. Uniqueness and overlap are enforced by
/// unique indexes and EXCLUDE constraints, which are not subject to the
/// anomalies Read Committed permits, so raising the level would buy nothing and
/// cost serialisation failures under concurrent grants.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly LigatureDbContext _dbContext;

    public UnitOfWork(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Enlist in a transaction someone else owns rather than opening a
        // second one, which Npgsql rejects outright. The outer owner commits.
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            var enlisted = await operation(cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return enlisted;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        // The whole unit — begin, work, save, commit — runs inside the strategy
        // delegate. If EnableRetryOnFailure is switched on later, a retry must
        // replay the entire transaction; a BeginTransactionAsync outside this
        // delegate would throw the moment retries were enabled.
        //
        // Caveat that comes with that: a retry re-invokes the operation on a
        // change tracker that still holds the previous attempt's entries. The
        // operation must therefore be idempotent in its database effects.
        return await strategy.ExecuteAsync(
            async ct =>
            {
                await using var transaction = await _dbContext.Database
                    .BeginTransactionAsync(ct);

                var result = await operation(ct);

                await _dbContext.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);

                return result;
            },
            cancellationToken);
    }
}
