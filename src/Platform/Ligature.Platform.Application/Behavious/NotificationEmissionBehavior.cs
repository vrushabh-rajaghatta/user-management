using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// The transactional half of notification enqueue (NOT-P1 step 1).
///
/// Runs INSIDE behaviour 6's transaction and OUTSIDE behaviour 7, so on the way
/// out the audit rows are written first and the Pending row second — both on
/// the command's transaction, committing together or not at all. The token and
/// the record that we intend to deliver it commit as one fact.
///
/// It adds to the unit of work rather than saving: behaviour 6 calls
/// SaveChanges once the whole inner chain has returned, which is after the
/// handler has already flushed the token this row's foreign key points at.
///
/// WHY THE CLEAR IS HERE AND NOT IN THE OUTER BEHAVIOUR. This behaviour sits
/// inside the execution strategy's delegate, so a retried unit of work
/// re-invokes it and the clear runs once per ATTEMPT. The post-commit
/// behaviour sits outside that boundary and runs once per DISPATCH; it could
/// not clear per attempt even if it wanted to. Without this, a replayed
/// transaction would carry the rolled-back attempt's declaration — holding a
/// plaintext for a token that no longer exists — alongside the one that
/// committed.
///
/// The handler declares; this writes. A User Management handler never
/// references a notification table (D-N1-07).
/// </summary>
internal sealed class NotificationEmissionBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly INotificationEmissionScope _scope;
    private readonly INotificationRepository _repository;

    public NotificationEmissionBehavior(
        INotificationEmissionScope scope,
        INotificationRepository repository)
    {
        _scope = scope;
        _repository = repository;
    }

    public async Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        // A replayed attempt starts clean. The declarations of the attempt that
        // rolled back describe tokens that were rolled back with it.
        _scope.ClearDeclarations();

        var result = await next(cancellationToken);

        foreach (var declaration in _scope.Declarations)
        {
            await _repository.AddAsync(
                Notification.CreatePending(
                    declaration.NotificationId,
                    declaration.NotificationType,
                    declaration.TokenId,
                    declaration.Recipient),
                cancellationToken);
        }

        return result;
    }
}
