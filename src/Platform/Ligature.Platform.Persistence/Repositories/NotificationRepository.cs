using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Repositories;

/// <summary>
/// Persists the Pending notification row.
///
/// This type never sees a token plaintext, in the same way and for the same
/// reason UserTokenRepository never does: the entity handed to it has no field
/// for one. N16 is structural rather than a matter of care here — there is no
/// column to write it to and no property to read it from, so the plaintext
/// cannot reach the database through this path even by mistake.
/// </summary>
public sealed class NotificationRepository : INotificationRepository
{
    private readonly LigatureDbContext _dbContext;

    public NotificationRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save, so a
    /// notification cannot be committed without the token it refers to.
    /// </summary>
    public Task AddAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _dbContext.Add(notification);

        return Task.CompletedTask;
    }
}
