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
        // The page is chosen FIRST, and activationPending derived only for its
        // rows. EF places a Take followed by another OrderBy in a subquery, and
        // a subquery with a LIMIT is a planner fence. Measured at 100,000 users
        // (docs/requirements.md, amendment 1): derived in the same SELECT,
        // PostgreSQL hashed both existence tests over the whole of
        // user_identity and credential on every page (first page 15 -> 87 ms,
        // deep page 103 -> 560 ms). Derived over the page, each test is an index
        // probe per returned row (11 ms and 88 ms). The outer order is the same
        // contract order; SQL does not carry a subquery's order out of it.
        var page = _dbContext.Set<User>()
            .AsNoTracking()
            .Where(x => x.ActorType == ActorType.Human)
            .OrderBy(x => EF.Functions.Collate(x.DisplayName, Collation))
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(limit);

        var rows = await page
            .OrderBy(x => EF.Functions.Collate(x.DisplayName, Collation))
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.DisplayName,
                x.Email,

                // USR-Q1 amendment 1 (D1), exactly and nothing broader: at
                // least one local identity, and no credential on any identity
                // of the user. It takes no part in which rows or in what order.
                ActivationPending =
                    _dbContext.Set<UserIdentity>().Any(i =>
                        i.UserId == x.Id && i.IdentityType == IdentityType.Local)
                    && !_dbContext.Set<UserIdentity>()
                        .Where(i => i.UserId == x.Id)
                        .Any(i => _dbContext.Set<Credential>().Any(c => c.UserIdentityId == i.Id)),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new UserListRow(x.Id, x.DisplayName, x.Email?.Value, x.ActivationPending, UserStatus.Active /* stub: USR-Q1 Amendment 2 */))
            .ToList();
    }
}
