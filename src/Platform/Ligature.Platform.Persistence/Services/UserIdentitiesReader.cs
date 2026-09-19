using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserIdentities;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// IDN-Q1 GetUserIdentities, as amended (docs/requirements.md, "IDN-Q1
/// GetUserIdentities and Unlock on the User detail page"): every identity of
/// one HUMAN user — local and external, active and inactive — oldest first,
/// ties by id. The System actor, like an unknown user, reads as null.
///
/// THE LOCK STATE IS A READ-TIME PROJECTION, not identity state: the
/// credential's LockedUntil is read, and Locked is decided here against the
/// instant the handler passes in, by CRD-C6's rule — a lock currently in force
/// (LockedUntil &gt; now). An expired lock, failures without a lock and no
/// credential all read as not locked, with no instant. The failure count and
/// the subject id are never read.
/// </summary>
public sealed class UserIdentitiesReader : IUserIdentitiesReader
{
    private readonly LigatureDbContext _dbContext;

    public UserIdentitiesReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserIdentityView>?> ReadAsync(
        UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var human = await _dbContext.Set<User>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == userId && x.ActorType == ActorType.Human, cancellationToken);

        if (!human)
            return null;

        var rows = await (
                from identity in _dbContext.Set<UserIdentity>().AsNoTracking()
                where identity.UserId == userId
                join credential in _dbContext.Set<Credential>().AsNoTracking()
                    on identity.Id equals credential.UserIdentityId into credentials
                from credential in credentials.DefaultIfEmpty()
                orderby identity.CreatedAt, identity.Id
                select new
                {
                    identity.Id,
                    identity.IdentityType,
                    identity.IdentityProvider,
                    identity.Username,
                    identity.Status,
                    DeactivatedAt = identity.Deactivation == null ? (DateTimeOffset?)null : identity.Deactivation.At,
                    LockedUntil = credential == null ? null : credential.LockedUntil,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x =>
            {
                // CRD-C6's rule, at this read's instant.
                var locked = x.LockedUntil is { } until && until > now;

                return new UserIdentityView(
                    x.Id,
                    x.IdentityType,
                    x.IdentityProvider.Value,
                    x.Username,
                    x.Status,
                    x.DeactivatedAt,
                    locked,
                    locked ? x.LockedUntil : null);
            })
            .ToList();
    }
}
