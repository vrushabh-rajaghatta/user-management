using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class UserIdentityRepository : IUserIdentityRepository
{
    private readonly LigatureDbContext _dbContext;

    public UserIdentityRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// A user-facing validation affordance, NOT the uniqueness guarantee.
    /// UI7 — ux_user_identity_local_username — remains the authority.
    ///
    /// Note what this predicate does NOT contain: any filter on Status. UI7 is
    /// ABSOLUTE, spanning deactivated rows, and that asymmetry with the email
    /// rule is deliberate. A departed employee's address may be reissued; their
    /// username may not, because reuse would make historical logs ambiguous
    /// about which person a line refers to.
    ///
    /// Raw SQL rather than LINQ, but for a different reason than the email
    /// check. Username carries no value converter, so LINQ does translate here,
    /// to "lower(username) = @p". The catch is the parameter: EF evaluates
    /// ToLower() client-side under CurrentCulture, and .NET's fold is not
    /// PostgreSQL's. Under tr-TR, "IZMIR".ToLower() is 'ızmır' while the
    /// database's lower() under en_US.utf8 gives 'izmir' — the pre-check would
    /// report a username free that the index considers taken. Keeping both
    /// sides inside PostgreSQL means the check and the constraint fold
    /// identically by construction rather than by coincidence of culture.
    /// </summary>
    public async Task<bool> ExistsWithUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return await _dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1
                    FROM "user_identity"
                    WHERE lower("username") = lower({username})
                      AND "identity_type" = 'Local'
                      AND "username" IS NOT NULL
                ) AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save.
    /// </summary>
    public Task AddAsync(UserIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        _dbContext.Add(identity);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<UserIdentity?> FindLocalByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // Resolved in two steps, deliberately.
        //
        // The predicate has to run in PostgreSQL to use the same lower() as
        // ux_user_identity_local_username — EF would fold the parameter in .NET
        // under CurrentCulture, and that fold is not the database's, so a
        // lookup could refuse a sign-in for a username the index considers
        // taken. But FromSql cannot materialise this entity: UserIdentity owns
        // a DeactivationStamp, and EF expects the owned type's property names
        // ("Deactivation_At") rather than the column names a SELECT * returns.
        //
        // So the predicate returns a key, and the entity is loaded by that key
        // — tracked, because the caller carries it into session creation and it
        // must share a change tracker with the credential it writes.
        var ids = await _dbContext.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM user_identity
                WHERE lower(username) = lower({username})
                  AND identity_type = 'Local'
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count != 1)
            return null;

        var id = new UserIdentityId(ids[0]);

        return await _dbContext.Set<UserIdentity>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserIdentity?> FindAsync(
        UserIdentityId userIdentityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIdentityId);

        return await _dbContext.Set<UserIdentity>()
            .FirstOrDefaultAsync(
                x => x.Id == userIdentityId, cancellationToken);
    }
}
