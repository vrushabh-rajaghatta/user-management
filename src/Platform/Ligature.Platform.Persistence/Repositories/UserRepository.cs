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
}
