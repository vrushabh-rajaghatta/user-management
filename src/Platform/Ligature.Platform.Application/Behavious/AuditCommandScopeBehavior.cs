using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// The command's audit scope, and the autonomous half of emission.
///
/// This sits OUTSIDE the transaction behaviour, which is what makes the
/// autonomous path autonomous: by the time it writes, the command's
/// transaction has already committed or rolled back, and what it writes is
/// on a connection of its own either way.
///
/// It also owns the command's audit identity — the OperationId every record
/// of this command shares (AR15) and the OccurredAt they are stamped with
/// (behaviour 16). Those belong to the COMMAND, not to either transaction,
/// and they have to outlive the transactional one: a record written after
/// the commit still belongs to the same operation. That is why the scope
/// moved out here rather than staying with behaviour 6.
///
///     audit command scope
///       ├── transaction
///       │     ├── transactional emission
///       │     └── handler
///       └── autonomous emission (its own transaction)
///
/// FAILURE SEMANTICS. Frozen (E2b):
///
///   - The command succeeded and the autonomous write fails: the request
///     fails. The business change stays committed — it was committed before
///     this ran, and undoing it is not on offer — but nobody is told an
///     action was recorded when it was not.
///   - The command failed and the autonomous write also fails: the original
///     exception wins, unchanged, because it is the one that explains what
///     the caller did. The audit failure is attached to it rather than
///     replacing it, and the host logs it.
///   - Nothing is ever swallowed. A trail that quietly drops records is not
///     evidence, and it would drop them exactly when someone was attacking.
///
/// THE V1 AUTONOMOUS EMISSION CRASH WINDOW. Between the command's commit and
/// the autonomous commit the process can die, and that record is then lost.
/// This is accepted by the frozen failure semantics, not overlooked: closing
/// it means putting the record in the command's transaction, which is the
/// one thing these events cannot do.
/// </summary>
internal sealed class AuditCommandScopeBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// The key under which an autonomous write failure is attached to a
    /// command's own exception. Read by the host, which logs it.
    /// </summary>
    internal const string AuditFailureKey = "Ligature.AutonomousAuditFailure";

    private readonly IAuditEmissionScope _scope;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEventCatalogue _catalogue;
    private readonly IAutonomousAuditRecordWriter _writer;
    private readonly IClock _clock;

    public AuditCommandScopeBehavior(
        IAuditEmissionScope scope,
        IExecutionContext executionContext,
        IAuditEventCatalogue catalogue,
        IAutonomousAuditRecordWriter writer,
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
        // Once per dispatch, and outside the retryable unit: a retried
        // attempt is the same command, and every record it commits — on
        // either path — shares one OperationId (AR15).
        using var open = _scope.BeginCommand(
            operationId: Guid.CreateVersion7(),
            occurredAt: _clock.UtcNow);

        TResult result;

        try
        {
            result = await next(cancellationToken);
        }
        catch (Exception failure)
        {
            // The command failed, which is often exactly what the autonomous
            // records describe. They are written anyway, and this exception
            // survives whatever happens to them.
            try
            {
                await WriteAutonomousAsync(cancellationToken);
            }
            catch (Exception auditFailure)
            {
                failure.Data[AuditFailureKey] = auditFailure.ToString();
            }

            throw;
        }

        try
        {
            await WriteAutonomousAsync(cancellationToken);
        }
        catch (Exception auditFailure)
        {
            throw new InvalidOperationException(
                "Audit emission defect — the command succeeded and its change "
                + "is committed, but a record that must outlive it could not be "
                + "written. The request fails rather than report success for "
                + "something the trail does not contain; the business change "
                + "stands and is not undone by this.",
                auditFailure);
        }

        return result;
    }

    private async Task WriteAutonomousAsync(CancellationToken cancellationToken)
    {
        // Routed by the CATALOGUE, not by what the handler intended: the
        // write path is the catalogue's to decide (ET6). A code the catalogue
        // does not know is left to the transactional path, which is where its
        // defect is raised, rather than being silently dropped by both.
        var declarations = _scope.Declarations
            .Where(x => _catalogue.Find(x.Code, x.Version)?.WritePath == AuditWritePath.Autonomous)
            .ToList();

        if (declarations.Count == 0)
            return;

        var permitted = AuditDeclarations.For(typeof(TCommand))
            ?? throw new InvalidOperationException(
                $"Audit emission defect — {typeof(TCommand).Name} declared "
                + $"{declarations.Count} autonomous audit event(s) but is not "
                + "registered in AuditDeclarations, so nothing verified its "
                + "codes at start (IMPL-08).");

        var rows = AuditRecordAssembler.Assemble(
            declarations,
            permitted,
            ActorSnapshot.FromContext(_executionContext),
            _catalogue,
            AuditWritePath.Autonomous,
            _scope.OperationId,
            _scope.OccurredAt,
            capturedAt: _clock.UtcNow);

        await _writer.WriteAsync(rows, cancellationToken);
    }
}
