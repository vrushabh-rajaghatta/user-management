using Ligature.Platform.Application.Notifications;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Owns the command-scoped lifetime of notification declarations, and hands
/// them to the sender once the command has succeeded.
///
/// POSITION. Outside the audit command scope, and therefore outside the
/// transaction. Two consequences follow, and both are deliberate:
///
///   - It runs once per DISPATCH, not once per transaction attempt. The
///     per-attempt clear therefore belongs to the emission behaviour inside
///     behaviour 6, which is where it is.
///   - It is outside AuditCommandScopeBehavior, which fails the request when
///     an autonomous audit write fails after the command committed. So a
///     command whose result never reached the API layer hands off nothing: the
///     send follows the result, and there was no result. The row stays Pending
///     and the sweeper closes it as Abandoned — the honest record, since nobody
///     was told the command succeeded.
///
/// THE HANDOFF CANNOT FAIL THE COMMAND. TryHandoff neither blocks nor throws,
/// so a saturated sender cannot put its backlog on the command's critical path
/// and cannot fail a command whose user, identity and token are committed. A
/// refusal is an ordinary outcome: the declaration is dropped, the plaintext
/// goes with it, and the sweeper closes the row.
///
/// The enqueue happens here rather than literally after the HTTP response is
/// written, and that is the intended reading of IMPL-N01: what must follow the
/// command result is the TRANSPORT, and the bounded channel guarantees that
/// structurally, because the send runs on a consumer thread rather than this
/// one. Placing the enqueue itself in the host would put a token plaintext into
/// host-layer state to buy nothing observable.
///
/// The lifetime is not incidental. Disposing it drops every declaration, and
/// with them the last reference to any token plaintext the command declared —
/// on the throw path as well as the success path.
/// </summary>
internal sealed class NotificationPostCommitBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly INotificationEmissionScope _scope;
    private readonly INotificationHandoff? _handoff;

    /// <param name="handoff">
    /// Absent when mail is not configured. Nothing is handed off in that case
    /// and nothing is retained — the committed Pending row is closed by the
    /// sweeper as Abandoned, which is exactly what it means.
    /// </param>
    public NotificationPostCommitBehavior(
        INotificationEmissionScope scope,
        INotificationHandoff? handoff = null)
    {
        _scope = scope;
        _handoff = handoff;
    }

    public async Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        using var open = _scope.BeginCommand();

        // If this throws, the foreach below never runs and the using clears
        // the declarations. No result, no send.
        var result = await next(cancellationToken);

        foreach (var declaration in _scope.Declarations)
        {
            // Null when there is no consumer. Handing a live plaintext to a
            // queue nothing drains would retain it for the life of the process.
            _handoff?.TryHandoff(declaration);
        }

        return result;
    }
}
