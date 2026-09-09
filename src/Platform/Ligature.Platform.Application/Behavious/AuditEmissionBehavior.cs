using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Behaviour 7 — transactional emission, with 13, 14 and 16 folded in.
///
/// Runs INSIDE behaviour 6's transaction: it invokes the handler, and when
/// the handler returns it takes what was declared, attaches the actor
/// snapshot and the command's OperationId and clock, resolves each event
/// against the catalogue, validates it, and writes — all before behaviour 6
/// commits. No handler references an audit table, and this behaviour cannot
/// be omitted by one.
///
/// A command that declares nothing writes nothing. That is the case for
/// every command not yet wired to its events and for commands whose events
/// are all autonomous (Slice B); the pipeline does not invent a record.
///
/// Every refusal is a defect: the exception propagates, behaviour 6's unit
/// of work rolls the transaction back, and the host answers with a
/// no-detail 500 (section 15.1). It is never a domain error, because a
/// record that fails validation is not evidence and a user-facing message
/// would invite a retry that cannot succeed.
///
/// Internal, like the writer it holds: nothing outside the pipeline
/// assembly constructs the one component that writes audit rows.
/// </summary>
internal sealed class AuditEmissionBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IAuditEmissionScope _scope;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEventCatalogue _catalogue;
    private readonly IAuditRecordWriter _writer;
    private readonly IClock _clock;

    public AuditEmissionBehavior(
        IAuditEmissionScope scope,
        IExecutionContext executionContext,
        IAuditEventCatalogue catalogue,
        IAuditRecordWriter writer,
        IClock clock)
    {
        _scope = scope;
        _executionContext = executionContext;
        _catalogue = catalogue;
        _writer = writer;
        _clock = clock;
    }

    public async Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        // A replayed attempt starts clean; the declarations of the attempt
        // that rolled back are not evidence of anything.
        _scope.ClearDeclarations();

        var result = await next(cancellationToken);

        var declarations = _scope.Declarations;

        if (declarations.Count == 0)
            return result;

        var permitted = AuditDeclarations.For(typeof(TCommand))
            ?? throw new InvalidOperationException(
                $"Audit emission defect — {typeof(TCommand).Name} declared "
                + $"{declarations.Count} audit event(s) but is not registered in "
                + "AuditDeclarations, so nothing verified its codes at start "
                + "(IMPL-08). The command has been rolled back.");

        // Behaviour 16: OccurredAt was fixed at handler start by behaviour 6;
        // CapturedAt is now, at emission. CreatedAt is the database's.
        var rows = AuditRecordAssembler.Assemble(
            declarations,
            permitted,
            ActorSnapshot.FromContext(_executionContext),
            _catalogue,
            _scope.OperationId,
            _scope.OccurredAt,
            capturedAt: _clock.UtcNow);

        await _writer.WriteAsync(rows, cancellationToken);

        return result;
    }
}
