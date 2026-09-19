using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserProfile;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// USR-Q1 GetUser v2 (docs/requirements.md, "USR-Q1 GetUser v2 and the User
/// detail page"): the names, and the list row's email, status and
/// activationPending — read through the SAME projection the list uses, so the
/// two agree. HUMAN users only, the scope the list uses, so the System actor
/// reads as unknown.
///
/// Nothing else: no identities and no assignments (the USR-Q1 composition
/// amendment — those are IDN-Q1's under identity.read and AUT-Q2's under
/// role.read), and no actor type or deactivation time.
/// </summary>
public sealed class UserProfileReader : IUserProfileReader
{
    private readonly LigatureDbContext _dbContext;

    public UserProfileReader(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<UserProfileResult?> ReadAsync(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var row = await _dbContext.Set<User>()
            .AsNoTracking()
            .Where(x => x.Id == userId && x.ActorType == ActorType.Human)
            .Select(UserLifecycleProjection.Of(_dbContext))
            .SingleOrDefaultAsync(cancellationToken);

        // First and last name are non-null for humans (ck_app_user_human_names).
        return row is null
            ? null
            : new UserProfileResult(
                row.UserId, row.FirstName!, row.LastName!, row.DisplayName,
                row.Email?.Value, row.Status, row.ActivationPending);
    }
}
