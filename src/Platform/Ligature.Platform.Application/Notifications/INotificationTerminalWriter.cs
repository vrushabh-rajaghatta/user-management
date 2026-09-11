using Ligature.Platform.Domain.Notifications;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// How one notification ended, as the sender observed it. Every outcome the
/// sender can produce carries an AttemptedAt, because the sender began — only
/// the sweeper's Abandoned lacks one, and the sweeper does not come through
/// here.
/// </summary>
internal sealed record NotificationOutcome(
    NotificationStatus Status,
    NotSentReason? Reason,
    DateTimeOffset AttemptedAt,
    string? TransportMessageId);

/// <summary>
/// The single guarded UPDATE that takes a row out of Pending (NOT-P2 step 6),
/// on its own short transaction on a pooled connection, never enlisting in an
/// ambient one.
///
/// It may retry ONCE on a transient database error. If the first write had in
/// fact committed, the N9 trigger refuses the second — and that refusal is
/// TREATED AS SUCCESS, because it proves the row already reached the state we
/// were trying to write. N9 is therefore three things at once: the lifecycle
/// guard, the concurrency protection, and the idempotency mechanism this retry
/// depends on.
///
/// If the write ultimately fails the row stays Pending and the sweeper closes
/// it as Abandoned — the honest record, since the outcome was never persisted.
/// </summary>
internal interface INotificationTerminalWriter
{
    Task CloseAsync(
        NotificationId notificationId,
        NotificationOutcome outcome,
        CancellationToken cancellationToken);
}

/// <summary>
/// NOT-P3. Closes rows the sender was never observed to begin.
///
/// Idempotent by construction: a swept row no longer matches the predicate.
/// Batched, each batch its own short transaction, using the partial index
/// shipped with the table in Phase A.
///
/// AttemptedAt is left untouched and therefore null, which is precisely what
/// distinguishes Abandoned from every other terminal reason (N6): we do not
/// know what happened, including the case where the transport had in fact
/// accepted the message before the process died.
/// </summary>
internal interface INotificationSweeper
{
    /// <returns>How many rows were closed.</returns>
    Task<int> SweepAsync(
        TimeSpan graceWindow,
        int batchSize,
        CancellationToken cancellationToken);
}
