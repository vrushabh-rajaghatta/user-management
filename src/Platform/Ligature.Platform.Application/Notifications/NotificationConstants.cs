namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// Release constants for the handoff and the sweeper.
///
/// Capacity and consumers are NOT independent knobs. Worst-case scheduling
/// latency — the first term of the §5.4 grace-window inequality — is roughly
///
///     (capacity ÷ consumers) × transport timeout
///
/// so these two values, the transport timeout and the grace window are one
/// decision. At 32 and 4 against a 10 second timeout that is an 80 second
/// bound, comfortably inside a 5 minute window.
///
/// Capacity is small on purpose for a second reason: every queued item holds a
/// live activation secret in memory, so capacity bounds how many plaintexts can
/// be resident at once. A deep queue would work against the decision the whole
/// context exists to serve. USR-C1 is an administrator creating a user — human
/// rate — so shedding at 32 is an overload signal, not routine backpressure.
///
/// PROVISIONAL. The §5.4 inequality also contains the hard transaction bound T,
/// which is parked and unenforced (AUD-O17), so the end-to-end guarantee is NOT
/// claimed. These values stand until the Phase F load measurement fixes them.
/// </summary>
internal static class NotificationConstants
{
    internal const int HandoffCapacity = 32;

    internal const int ConsumerCount = 4;

    /// <summary>
    /// N-O1, provisional. Must exceed the computed maximum duration of a send:
    /// scheduling latency + gate read + transport timeout + terminal write.
    /// Minutes, not hours — it has no relationship to token lifetime, since an
    /// Abandoned row does not affect the token.
    /// </summary>
    internal static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    internal const int SweepBatchSize = 100;

    /// <summary>
    /// How long the pump waits for in-flight sends when the host is stopping.
    /// Bounded on purpose: draining to completion would turn a deliberately
    /// lossy in-memory handoff into a durable queue, which is the one thing
    /// D-NOTIF-01 rules out. Whatever is not attempted becomes Abandoned.
    /// </summary>
    internal static readonly TimeSpan ShutdownDrain = TimeSpan.FromSeconds(5);
}
