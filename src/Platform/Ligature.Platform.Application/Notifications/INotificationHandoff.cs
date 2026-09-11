namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// The post-commit handoff: where a declaration crosses from the command's
/// thread to the sender's.
///
/// TryHandoff is NON-BLOCKING and NON-THROWING, and both are load-bearing
/// rather than conveniences. Blocking would put the sender's backlog on the
/// command's critical path and make the scheduling-latency bound unbounded,
/// which breaks the grace-window invariant at any value. Throwing would let a
/// full channel fail a command whose user, identity and token are already
/// committed.
///
/// A refusal is therefore an ordinary outcome, not an error: the item is
/// dropped, the plaintext goes with it, and the row stays Pending for the
/// sweeper to close as Abandoned. That is the honest record — nobody observed
/// an attempt.
/// </summary>
internal interface INotificationHandoff
{
    /// <returns>
    /// True when the sender has taken ownership of the declaration. False when
    /// capacity is exhausted or the handoff is closed, in which case the caller
    /// must drop the declaration and must not retry.
    /// </returns>
    bool TryHandoff(NotificationDeclaration declaration);
}
