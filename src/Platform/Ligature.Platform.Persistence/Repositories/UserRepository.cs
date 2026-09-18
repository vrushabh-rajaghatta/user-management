using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly LigatureDbContext _dbContext;

    public UserRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// A user-facing validation affordance, NOT the uniqueness guarantee.
    ///
    /// AU3 — the partial unique index below — remains the authority. Two
    /// concurrent CreateUser commands can both pass this check and only one can
    /// pass the INSERT; the loser gets 23505, which the exception translation
    /// layer turns into a business-rule error. This method exists so the common
    /// case produces "that email is already in use" instead of a Postgres
    /// error string.
    ///
    /// The predicate is raw SQL copied from ux_app_user_active_human_email
    /// rather than LINQ, and that is not a performance choice. Email maps
    /// through a value converter, and EF cannot translate member access across
    /// a converter: x.Email.Value.ToLower() throws at translation time, while
    /// x.Email == target compiles to a case-sensitive "email = @p" that neither
    /// matches lower("email") nor uses the index. LINQ cannot express AU3 here.
    ///
    /// Note "status" &lt;&gt; 'Inactive' rather than = 'Active'. The two coincide
    /// while UserStatus has two values; mirroring the index literally means a
    /// third value could never silently put this check and the constraint into
    /// disagreement.
    /// </summary>
    public async Task<bool> ExistsActiveHumanWithEmailAsync(
        EmailAddress email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var value = email.Value;

        return await _dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1
                    FROM "app_user"
                    WHERE lower("email") = lower({value})
                      AND "actor_type" = 'Human'
                      AND "status" <> 'Inactive'
                      AND "email" IS NOT NULL
                ) AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save, so that a
    /// command writing several aggregates cannot half-commit.
    /// </summary>
    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        _dbContext.Add(user);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<User?> FindAsync(
        UserId userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await _dbContext.Set<User>()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// FOR UPDATE holds only inside a transaction, so every caller runs this in
    /// the unit of work's, and first: EF returns an entity it already tracks
    /// without re-reading it, so a user loaded earlier in the same scope would
    /// be returned as it was read, not as it is under the lock. The three
    /// callers (USR-C4, USR-C5, AUT-C1) lock before they load anything else.
    /// </remarks>
    public async Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        // The lock, then the load, as two statements in one transaction. Not
        // FromSql over the entity: its owned deactivation stamp does not
        // compose with a raw SELECT *, and the lock needs no columns anyway.
        // A row that does not exist locks nothing and loads as null.
        await _dbContext.Database
            .SqlQuery<int>($"""SELECT 1 AS "Value" FROM "app_user" WHERE "id" = {userId.Value} FOR UPDATE""")
            .ToListAsync(cancellationToken);

        return await _dbContext.Set<User>()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }
}
