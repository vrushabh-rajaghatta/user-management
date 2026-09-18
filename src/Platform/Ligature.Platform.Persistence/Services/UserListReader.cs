using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// USR-Q1 — the list read, as one statement: no count, no second query.
///
/// THE COLLATION IS EXPLICIT, and must stay so. The database's default is
/// libc en_US.utf8 everywhere, and it still orders differently by platform:
/// musl (the deployment image) compares bytes, glibc (the test database)
/// collates linguistically. ICU "unicode" orders identically on both
/// (docs/requirements.md, USR-Q1 "Sorting"). UserId needs none — uuid compares
/// by value.
///
/// No index supports this order, deliberately; the measured plans are in the
/// contract.
///
/// AsNoTracking: nothing read here is ever written back.
/// </summary>
public sealed class UserListReader : IUserListReader
{
    /// <summary>PostgreSQL's ICU root collation.</summary>
    private const string Collation = "unicode";

    private readonly LigatureDbContext _dbContext;

    public UserListReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserListRow>> ReadAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Set<User>()
            .AsNoTracking()
            .Where(x => x.ActorType == ActorType.Human)
            .OrderBy(x => EF.Functions.Collate(x.DisplayName, Collation))
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .Select(x => new { x.Id, x.DisplayName, x.Email })
            .ToListAsync(cancellationToken);

        return rows
            // RED STUB (USR-Q1 amendment 1): a constant, so the tests that pair a
            // pending user with an activated one fail whichever constant it is.
            .Select(x => new UserListRow(x.Id, x.DisplayName, x.Email?.Value, ActivationPending: false))
            .ToList();
    }
}
