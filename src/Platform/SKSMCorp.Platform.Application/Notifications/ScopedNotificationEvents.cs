using SKSMCorp.Platform.Domain.Notifications;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Notifications;

/// <summary>
/// The pipeline's side of <see cref="INotificationEvents"/>: what the
/// post-commit behaviour opens and the emission behaviour reads. Internal, so a
/// handler injecting INotificationEvents sees only Emit.
/// </summary>
internal interface INotificationEmissionScope
{
    IReadOnlyList<NotificationDeclaration> Declarations { get; }

    /// <summary>
    /// Opens the command. Establishing while one is already open is a nested
    /// dispatch, which is a defect, and throws. Dispose closes it and drops
    /// every declaration, and with them the last reference to any plaintext
    /// they carried.
    /// </summary>
    IDisposable BeginCommand();

    /// <summary>
    /// Forgets declarations made so far in this command. The emission
    /// behaviour calls it before invoking the handler: a unit of work that
    /// retries replays the handler, and the attempt that rolled back declared
    /// a notification for a token that no longer exists.
    /// </summary>
    void ClearDeclarations();
}

/// <summary>
/// Collects a command's declared notifications for the scope of that command.
///
/// One instance per DI scope, but — as with the audit collector — the LIFETIME
/// IS A COMMAND, not the scope. A scope may dispatch several commands, and a
/// declaration that outlived its command would attach a live activation secret
/// to the next one. That is a sharper hazard here than it is for audit, where
/// the equivalent leak is a false record rather than a misdirected credential.
///
/// Deliberately separate from ScopedAuditEvents rather than sharing a combined
/// "declarations" abstraction. The two are different capabilities with
/// different failure semantics: an audit declaration that cannot be written
/// fails the request, and a notification that cannot be sent must not.
/// </summary>
internal sealed class ScopedNotificationEvents
    : INotificationEvents, INotificationEmissionScope
{
    private readonly List<NotificationDeclaration> _declarations = [];

    private bool _open;
    private object? _token;

    public IReadOnlyList<NotificationDeclaration> Declarations => _declarations;

    public void Emit(
        NotificationType notificationType,
        UserToken token,
        string recipient,
        string plaintextToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextToken);

        // N14, in the single writer. Only a code defect reaches this — every
        // call site passes a literal type beside the token it just issued — so
        // it is an emission defect and fails the command, never a business
        // refusal a caller could provoke. The agreement is a property of the
        // arguments alone, so it is checked first; a declaration outside a
        // command is refused below either way.
        if (!NotificationTokenAgreement.Permits(notificationType, token.TokenType))
        {
            throw new InvalidOperationException(
                "Notification emission defect — a "
                + $"'{notificationType}' notification was declared for a "
                + $"'{token.TokenType}' token. The notification type must agree "
                + "with the type of the token it delivers (N14).");
        }

        if (!_open)
        {
            // A handler running outside a dispatched command — a direct call,
            // or a pipeline missing the post-commit behaviour — has nothing
            // that will ever drain this. Refusing here is what stops a
            // plaintext token being handed to a collector nobody reads.
            throw new InvalidOperationException(
                "Notification emission defect — a notification was declared "
                + "outside a dispatched command. Notifications are enqueued by "
                + "the pipeline inside the issuing command's transaction; a "
                + "handler invoked directly has no such transaction, and the "
                + "declaration would hold a token plaintext that nothing "
                + "would ever send or discard.");
        }

        _declarations.Add(
            new NotificationDeclaration(
                NotificationId.New(),
                notificationType,
                token.Id,
                recipient,
                plaintextToken));
    }

    public IDisposable BeginCommand()
    {
        if (_open)
        {
            throw new InvalidOperationException(
                "Notification emission defect — a command is already open on "
                + "this scope. Commands are not nested; a second dispatch "
                + "inside a handler would attach its notifications to the "
                + "wrong command.");
        }

        _open = true;
        _token = new object();
        _declarations.Clear();

        return new CommandLifetime(this, _token);
    }

    public void ClearDeclarations() => _declarations.Clear();

    /// <summary>
    /// Token-checked for the same reason the audit collector's is: a stale
    /// handle must not close a later command.
    /// </summary>
    private sealed class CommandLifetime : IDisposable
    {
        private readonly ScopedNotificationEvents _owner;
        private readonly object _token;

        internal CommandLifetime(ScopedNotificationEvents owner, object token)
        {
            _owner = owner;
            _token = token;
        }

        public void Dispose()
        {
            if (!ReferenceEquals(_owner._token, _token))
                return;

            _owner._open = false;
            _owner._token = null;
            _owner._declarations.Clear();
        }
    }
}
