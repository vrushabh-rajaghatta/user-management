using Ligature.Platform.Domain.Notifications;

namespace Ligature.Platform.Application.Abstractions;

public interface INotificationRepository
{
    /// <summary>
    /// Adds a Pending notification to the issuing command's unit of work.
    ///
    /// There is deliberately nothing else here. Notification rows are closed by
    /// a single guarded UPDATE on the sender's own connection, outside every
    /// ambient transaction (IMPL-N02), so the terminal write never travels
    /// through this interface — and nothing reads notifications until NOT-Q1
    /// arrives with slice N2.
    /// </summary>
    Task AddAsync(
        Notification notification,
        CancellationToken cancellationToken);
}
