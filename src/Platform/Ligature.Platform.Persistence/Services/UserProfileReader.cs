using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserProfile;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// USR-Q1 GetUser, narrow v1 (docs/requirements.md, "USR-C2 — Update User
/// Profile, and USR-Q1 GetUser (narrow v1)"): exactly the four profile fields,
/// for HUMAN users only — the scope the list uses — so the System actor reads
/// as unknown. None of the catalogue's other GetUser fields.
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
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);

        // First and last name are non-null for humans (ck_app_user_human_names).
        return row is null
            ? null
            // COMPILE-ONLY STUB (red tests): v2's fields are not read yet.
            : new UserProfileResult(row.Id, row.FirstName!, row.LastName!, row.DisplayName, null, UserStatus.Active, false);
    }
}
