using Ligature.Platform.Application.Notifications;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Owns the command-scoped lifetime of notification declarations, and — from
/// slice N1 Phase C — the post-commit handoff to the bounded sender.
///
/// POSITION. Outside the audit command scope, and therefore outside the
/// transaction. Two consequences follow, and both are deliberate:
///
///   - It runs once per DISPATCH, not once per transaction attempt. The
///     per-attempt clear therefore belongs to the emission behaviour inside
///     behaviour 6, which is where it is.
///   - It is outside AuditCommandScopeBehavior, which fails the request when
///     an autonomous audit write fails after the command committed. Placing
///     the handoff here means a command whose result never reached the API
///     layer sends no mail: the send follows the result, and there was no
///     result. The row stays Pending and the sweeper closes it as Abandoned —
///     the honest record, since nobody was told the command succeeded.
///
/// WHAT IT DOES NOT DO YET. Phase B establishes the lifetime and nothing else.
/// There is no handoff here, and deliberately no placeholder for one: an
/// adapter that accepted a declaration and discarded it would be a delivery
/// mechanism that does not deliver, and the tests written against it would
/// prove a handoff that is not real. The bounded channel arrives in Phase C,
/// where the contract it needs — ownership of the declaration, capacity,
/// shedding, plaintext lifetime and shutdown — is settled together.
///
/// Until then a declared notification commits as a Pending row and is never
/// attempted, which the sweeper will later record as Abandoned. That is
/// strictly more truthful than the state it replaces, where the token was
/// discarded and nothing was written down at all.
///
/// The lifetime is not incidental. Disposing it drops every declaration, and
/// with them the last reference to any token plaintext the command declared.
/// </summary>
internal sealed class NotificationPostCommitBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly INotificationEmissionScope _scope;

    public NotificationPostCommitBehavior(INotificationEmissionScope scope)
        => _scope = scope;

    public async Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        using var open = _scope.BeginCommand();

        return await next(cancellationToken);
    }
}
