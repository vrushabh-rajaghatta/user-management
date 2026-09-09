namespace Ligature.Platform.Application.Audit;

/// <summary>
/// The pipeline's side of <see cref="IAuditEvents"/>: what behaviour 6 opens
/// and behaviour 7 reads. Internal, so a handler injecting IAuditEvents sees
/// only Emit.
/// </summary>
internal interface IAuditEmissionScope
{
    /// <summary>
    /// The identifier every record of this command shares (AR15, AUD-D20),
    /// minted by behaviour 6 once per dispatch.
    /// </summary>
    Guid OperationId { get; }

    /// <summary>The command clock at handler start (behaviour 16).</summary>
    DateTimeOffset OccurredAt { get; }

    IReadOnlyList<AuditEventDeclaration> Declarations { get; }

    /// <summary>
    /// Opens the command. Establishing while one is already open is a nested
    /// dispatch, which is a defect, and throws. Dispose closes it.
    /// </summary>
    IDisposable BeginCommand(Guid operationId, DateTimeOffset occurredAt);

    /// <summary>
    /// Forgets declarations made so far in this command. Behaviour 7 calls
    /// it before invoking the handler: a unit of work that retries replays
    /// the handler, and the declarations of the attempt that rolled back must
    /// not be written alongside the attempt that commits.
    /// </summary>
    void ClearDeclarations();
}

/// <summary>
/// Collects a command's declared events for the scope of that command.
///
/// One instance per DI scope, but — as with the execution context's authority
/// — the LIFETIME IS A COMMAND, not the scope. A scope may dispatch several
/// commands, and each has its own OperationId and its own declarations. A
/// declaration that outlived its command would be written under the next
/// command's operation, which is a false record.
/// </summary>
internal sealed class ScopedAuditEvents : IAuditEvents, IAuditEmissionScope
{
    private readonly List<AuditEventDeclaration> _declarations = [];

    private Guid? _operationId;
    private DateTimeOffset? _occurredAt;
    private object? _token;

    public Guid OperationId =>
        _operationId ?? throw new InvalidOperationException(
            "No command is open on this scope, so there is no OperationId. "
            + "Audit events are declared inside a dispatched command.");

    public DateTimeOffset OccurredAt =>
        _occurredAt ?? throw new InvalidOperationException(
            "No command is open on this scope, so there is no command clock.");

    public IReadOnlyList<AuditEventDeclaration> Declarations => _declarations;

    public AuditEventDeclaration Emit(string code, int version)
    {
        if (_operationId is null)
        {
            // A handler running outside a dispatched command — a direct call,
            // or a pipeline missing behaviour 6 — has no transaction for the
            // pipeline to write into. Refusing the declaration here is what
            // stops an event from being silently dropped.
            throw new InvalidOperationException(
                $"'{code}' was declared outside a dispatched command. Audit "
                + "events are emitted by the pipeline inside the command's "
                + "transaction; a handler invoked directly has no such "
                + "transaction and its events would never be written.");
        }

        var declaration = new AuditEventDeclaration(code, version);

        _declarations.Add(declaration);

        return declaration;
    }

    public IDisposable BeginCommand(Guid operationId, DateTimeOffset occurredAt)
    {
        if (_operationId is not null)
        {
            throw new InvalidOperationException(
                "A command is already open on this scope. Commands are not "
                + "nested; a second dispatch inside a handler would attribute "
                + "its events to the wrong operation.");
        }

        _operationId = operationId;
        _occurredAt = occurredAt;
        _token = new object();
        _declarations.Clear();

        return new CommandLifetime(this, _token);
    }

    public void ClearDeclarations() => _declarations.Clear();

    /// <summary>
    /// Token-checked for the same reason the authority handle is: a stale
    /// handle must not close a later command.
    /// </summary>
    private sealed class CommandLifetime : IDisposable
    {
        private readonly ScopedAuditEvents _owner;
        private readonly object _token;

        internal CommandLifetime(ScopedAuditEvents owner, object token)
        {
            _owner = owner;
            _token = token;
        }

        public void Dispose()
        {
            if (!ReferenceEquals(_owner._token, _token))
                return;

            _owner._operationId = null;
            _owner._occurredAt = null;
            _owner._token = null;
            _owner._declarations.Clear();
        }
    }
}
