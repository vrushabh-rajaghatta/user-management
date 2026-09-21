using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Queries.UserList;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Services;

/// <summary>
/// USR-Q2 — the list read, as one statement: no count, no second query.
///
/// THE COLLATION IS EXPLICIT, and must stay so. The database's default is
/// libc en_US.utf8 everywhere, and it still orders differently by platform:
/// musl (the deployment image) compares bytes, glibc (the test database)
/// collates linguistically. ICU "unicode" orders identically on both
/// (docs/requirements.md, USR-Q2 "Sorting"). UserId needs none — uuid compares
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

    private readonly SKSMCorpDbContext _dbContext;

    public UserListReader(SKSMCorpDbContext dbContext)
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
            // What each user IS (email, status, activationPending) comes from
            // the projection the detail read shares, so the two views cannot
            // disagree (UserLifecycleProjection). It takes no part in which
            // rows or in what order.
            .Select(UserLifecycleProjection.Of(_dbContext))
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new UserListRow(x.UserId, x.DisplayName, x.Email?.Value, x.ActivationPending, x.Status))
            .ToList();
    }
}
