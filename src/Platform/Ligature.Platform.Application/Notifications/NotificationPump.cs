namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// Drives notification delivery for the life of the host.
///
/// TWO MODES, and the second is not a degraded version of the first:
///
///     mail configured   consumers drain the handoff, and the sweeper runs
///     mail absent       the sweeper runs, and nothing is ever sent
///
/// The sweeper runs in BOTH because abandonment is not a delivery concern. A
/// Pending row means "nobody has been observed to attempt this", and that is
/// exactly as true on a host with no mail configured as on one whose sender
/// died mid-flight. Without it, a deployment without mail would accumulate
/// Pending rows for ever and the lifecycle would have no terminus.
///
/// There is deliberately no stand-in transport for the mail-absent case. One
/// that accepted a declaration and discarded it would be a delivery mechanism
/// that does not deliver, and the rows would claim an attempt nobody made.
///
/// NO DI SCOPES. The gate, the terminal writer and the sweeper each hold a
/// connection string and open their own connection (IMPL-N02), so every
/// collaborator here is a singleton and this needs no IServiceScopeFactory —
/// which also means no request-scoped execution context can leak into a
/// background thread, where it would be unset and would throw on use.
/// </summary>
internal sealed class NotificationPump : INotificationPump
{
    private readonly BoundedNotificationHandoff? _handoff;
    private readonly INotificationSweeper _sweeper;
    private readonly NotificationSender? _sender;
    private readonly TimeSpan _sweepInterval;
    private readonly TimeSpan _shutdownDrain;

    /// <param name="sender">
    /// Absent when mail is not configured, which is what selects sweep-only
    /// mode. There is deliberately no flag and no stand-in transport.
    /// </param>
    public NotificationPump(
        INotificationSweeper sweeper,
        BoundedNotificationHandoff? handoff = null,
        NotificationSender? sender = null,
        TimeSpan? sweepInterval = null,
        TimeSpan? shutdownDrain = null)
    {
        _handoff = handoff;
        _sweeper = sweeper;
        _sender = sender;
        _sweepInterval = sweepInterval ?? NotificationConstants.SweepInterval;
        _shutdownDrain = shutdownDrain ?? NotificationConstants.ShutdownDrain;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var drain = new CancellationTokenSource();

        var work = new List<Task> { SweepLoopAsync(stoppingToken) };

        for (var i = 0; _handoff is not null && _sender is not null
                 && i < NotificationConstants.ConsumerCount; i++)
        {
            work.Add(ConsumeAsync(drain.Token));
        }

        // Waited for explicitly rather than handled in a cancellation callback.
        // A callback races the workers: the sweep loop also ends on this token,
        // so it could return from here and dispose the registration before the
        // callback had closed the handoff — leaving a stopping host still
        // accepting work that nothing would ever drain.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping. This is the normal exit.
        }

        // Stopping does not cancel the consumers outright. It closes the
        // handoff so no new work arrives, then gives what is already in flight
        // a BOUNDED window to finish. Draining to completion would turn a
        // deliberately lossy in-memory handoff into a durable queue; cancelling
        // immediately would abandon sends that were seconds from done. What is
        // not attempted stays Pending and the sweeper closes it.
        _handoff?.Complete();

        drain.CancelAfter(_shutdownDrain);

        try
        {
            await Task.WhenAll(work);
        }
        finally
        {
            // Past the deadline nothing will attempt what is still queued, so
            // it is dropped rather than left resident holding token plaintexts.
            // In a finally because a faulted consumer must not be the reason a
            // secret outlives its disposition path.
            _handoff?.DiscardRemaining();
        }
    }

    private async Task ConsumeAsync(CancellationToken drainToken)
    {
        try
        {
            await foreach (var declaration in _handoff!.ReadAllAsync(drainToken))
            {
                try
                {
                    await _sender!.SendAsync(declaration, drainToken);
                }
                catch (OperationCanceledException) when (drainToken.IsCancellationRequested)
                {
                    // THE DEADLINE HAS PASSED — stop consuming, do not carry on
                    // to the next item. Swallowing this instead let the loop
                    // continue: each consumer raced through the remaining
                    // backlog, "attempting" every item into an already
                    // cancelled token and discarding it, so the deadline
                    // stopped nothing. Rethrowing ends the loop and leaves the
                    // rest queued for the discard below.
                    throw;
                }
                catch (Exception)
                {
                    // One failed send must not take the consumer down with it.
                    // SendAsync already records a transport failure on the row,
                    // so reaching here means the terminal write itself failed:
                    // the row stays Pending and the sweeper closes it as
                    // Abandoned. Deliberately swallowed — see the observability
                    // note below.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The drain deadline passed. Whatever was still queued is dropped
            // with its plaintext, and its row is swept to Abandoned.
        }
    }

    private async Task SweepLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sweepInterval);

        try
        {
            // Once on start, before the first tick. A host that has just
            // restarted is the likeliest source of rows nobody will ever
            // attempt — its predecessor died holding them — so waiting a whole
            // interval to notice would be exactly backwards.
            do
            {
                try
                {
                    await _sweeper.SweepAsync(
                        NotificationConstants.GraceWindow,
                        NotificationConstants.SweepBatchSize,
                        stoppingToken);
                }
                catch (Exception)
                {
                    // A database blip must not stop sweeping for the life of
                    // the process, and must not take the host down either.
                    //
                    // OBSERVABILITY GAP, stated rather than hidden: there is no
                    // logging in this repository yet (docs/architecture.md §7
                    // has it as target state), so a persistent sweep failure is
                    // currently silent. It is not a correctness gap — no row is
                    // lost or corrupted, and NOT-Q1 will show the backlog when
                    // slice N2 lands — but it is the first place a logger
                    // should go when one exists.
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host stopping.
        }
    }
}
