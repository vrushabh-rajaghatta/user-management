using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Behaviour 6 — transaction scope. Opens the command's transaction and
/// commits when everything inside has returned.
///
/// The OperationId and the command clock are NOT minted here. They belong to
/// the command rather than to this transaction, and they have to outlive it:
/// autonomous records are written after this behaviour has committed, and
/// they carry the same operation. AuditCommandScopeBehavior owns them, and
/// sits outside this.
///
/// This is what lets behaviour 7 write inside the command's transaction
/// without any handler changing. Handlers still call
/// IUnitOfWork.ExecuteInTransactionAsync, and the unit of work already
/// enlists in a transaction someone else owns: it runs the work, flushes,
/// and leaves the commit to the owner. That owner is now this behaviour. The
/// business rows are therefore flushed by the time the handler returns,
/// which AR10's foreign key to app_user requires, and the audit rows follow
/// on the same transaction, which invariant 16 requires. If the commit
/// fails, nothing was recorded; if it succeeds, everything was.
///
/// The unit of work is used for the outer transaction too, rather than a
/// second mechanism, so the EF execution strategy keeps its shape — begin,
/// work, save, commit inside one delegate. Enabling retries later replays
/// the whole unit, handler and emission alike; behaviour 7 clears the
/// declarations of a replayed attempt before the handler runs again.
///
/// The hard duration bound T (behaviour 6 amended, AUD-18) is NOT applied
/// here: the specification excludes it from Slice A (section 20.1), and it
/// is parked with Engineering (AUD-O17).
/// </summary>
internal sealed class TransactionScopeBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public TransactionScopeBehavior(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    public async Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(
            ct => next(ct),
            cancellationToken);
    }
}
