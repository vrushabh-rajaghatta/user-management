using System.Threading.Channels;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// The bounded in-process handoff. A singleton, because it outlives every
/// request that writes to it.
///
/// FullMode.Wait, combined with TryWrite and NEVER WriteAsync. That pairing is
/// what makes shedding both non-blocking and OBSERVABLE, and the distinction
/// matters more than it looks:
///
///   - Wait + WriteAsync would block a command on the sender's backlog, making
///     scheduling latency unbounded and breaking the grace-window invariant at
///     any value. Nothing here ever calls WriteAsync, and the class exposes no
///     way to.
///   - DropWrite + TryWrite would be non-blocking, but TryWrite RETURNS TRUE
///     when it drops — so a discarded activation secret would be
///     indistinguishable from a queued one, and no caller could ever count or
///     report a shed.
///   - DropOldest would discard the oldest activation secret to make room for a
///     newer one: no better, and harder to reason about.
///
/// Wait + TryWrite returns false the instant the channel is full, which is the
/// only shape that is both off the critical path and honest about what it did.
///
/// Shedding is not failure handling, it is the design. A refused item leaves a
/// committed Pending row, and the sweeper closes it as Abandoned — a record
/// that says "nobody observed an attempt", which is exactly true.
///
/// Nothing here is durable and nothing is meant to be. An outbox relay would
/// have to persist the payload, and the payload is a token plaintext.
/// </summary>
internal sealed class BoundedNotificationHandoff : INotificationHandoff
{
    private readonly Channel<NotificationDeclaration> _channel =
        Channel.CreateBounded<NotificationDeclaration>(
            new BoundedChannelOptions(NotificationConstants.HandoffCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
            });

    public bool TryHandoff(NotificationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        // TryWrite, never WriteAsync: TryWrite does not wait even in Wait
        // mode, so a full channel returns false immediately and the caller
        // learns it still owns the declaration and must drop it.
        return _channel.Writer.TryWrite(declaration);
    }

    internal IAsyncEnumerable<NotificationDeclaration> ReadAllAsync(
        CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Refuses further handoffs and lets the consumers finish what is already
    /// queued. Called when the host begins stopping, so that a command
    /// committing during shutdown does not hand off work nothing will drain.
    /// </summary>
    internal void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// Drops whatever is still queued once the bounded drain has expired.
    ///
    /// Completing the writer stops new work arriving but does NOT release what
    /// is already queued — those declarations stay reachable from this
    /// singleton, each holding a live activation secret, until the process
    /// exits. Normally that is immediate, but "the process usually exits
    /// shortly afterwards" is not a disposition path: past the drain deadline
    /// nothing is going to attempt these, so the reference is dropped here
    /// deliberately rather than left to collection.
    ///
    /// It attempts NOTHING — no transport, no terminal write, no abandonment.
    /// The rows are still Pending and belong to the ordinary sweep, which is
    /// the only thing entitled to call them Abandoned.
    /// </summary>
    /// <returns>How many were discarded.</returns>
    internal int DiscardRemaining()
    {
        var discarded = 0;

        while (_channel.Reader.TryRead(out _))
            discarded++;

        return discarded;
    }

    /// <summary>
    /// How many declarations are still queued. Internal, like the rest of this
    /// type — nothing outside the assembly can see the class at all, so this
    /// exposes nothing to production callers that they did not already have.
    /// </summary>
    internal int Queued => _channel.Reader.Count;
}
