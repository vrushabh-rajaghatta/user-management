using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// The two modes, and the shutdown boundary between them.
///
/// The mode is selected by whether a sender exists, not by a flag — so these
/// tests construct the pump the way the composition root does, with and without
/// one, rather than setting a switch.
/// </summary>
public sealed class NotificationPumpTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task With_no_mail_configured_it_still_sweeps()
    {
        var sweeper = new CountingSweeper();

        await RunAsync(new BoundedNotificationHandoff(), sweeper, sender: null);

        // The whole point of sweep-only mode. Without it a deployment with no
        // mail would accumulate Pending rows for ever and the lifecycle would
        // have no terminus.
        Assert.True(sweeper.Calls > 0);
    }

    [Fact]
    public async Task With_no_mail_configured_there_is_no_handoff_to_retain_a_plaintext()
    {
        // The composition root registers the handoff only alongside the
        // sender, so sweep-only mode has none at all. This is the invariant
        // behind coupling phases C and D: a queue with no consumer would hold
        // live token plaintexts in a singleton until the process exited.
        var pump = new NotificationPump(
            new CountingSweeper(),
            handoff: null,
            sender: null,
            sweepInterval: Tick);

        using var stopping = new CancellationTokenSource();

        var running = pump.RunAsync(stopping.Token);

        await Task.Delay(Tick * 5);
        await stopping.CancelAsync();

        // Runs and stops cleanly with nothing to drain.
        await running;
    }

    [Fact]
    public async Task With_mail_configured_it_sweeps_and_sends()
    {
        var handoff = new BoundedNotificationHandoff();
        var sweeper = new CountingSweeper();
        var transport = new RecordingTransport();
        var writer = new CountingWriter();

        handoff.TryHandoff(Declaration());
        handoff.TryHandoff(Declaration());

        await RunAsync(handoff, sweeper, Sender(transport, writer));

        Assert.True(sweeper.Calls > 0);
        Assert.Equal(2, transport.Sent.Count);
        Assert.Equal(2, writer.Closed);
    }

    [Fact]
    public async Task Stopping_refuses_new_work_before_the_drain()
    {
        var handoff = new BoundedNotificationHandoff();

        await RunAsync(handoff, new CountingSweeper(), sender: null);

        // The host has stopped, so the handoff is closed. A command that
        // committed during shutdown must not hand off work nothing will drain.
        Assert.False(handoff.TryHandoff(Declaration()));
    }

    [Fact]
    public async Task A_send_still_running_at_the_drain_deadline_is_left_for_the_sweeper()
    {
        var handoff = new BoundedNotificationHandoff();
        var writer = new CountingWriter();

        // Slower than the drain window it will be given.
        var transport = new SlowTransport(TimeSpan.FromSeconds(30));

        handoff.TryHandoff(Declaration());

        await RunAsync(
            handoff,
            new CountingSweeper(),
            Sender(transport, writer),
            drain: TimeSpan.FromMilliseconds(50));

        // Bounded, not "drain to completion": waiting indefinitely would turn a
        // deliberately lossy in-memory handoff into a durable queue. The row is
        // left Pending and becomes Abandoned — which is true, because the
        // outcome was never observed.
        Assert.Equal(0, writer.Closed);
    }

    [Fact]
    public async Task Declarations_left_after_the_drain_deadline_are_discarded()
    {
        var handoff = new BoundedNotificationHandoff();
        var writer = new CountingWriter();

        // Slower than the drain window, so the consumers are still holding
        // their items when the deadline passes and the rest never get taken.
        var transport = new SlowTransport(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 10; i++)
            handoff.TryHandoff(Declaration());

        await RunAsync(
            handoff,
            new CountingSweeper(),
            Sender(transport, writer),
            drain: TimeSpan.FromMilliseconds(50));

        // Nothing is still resident holding a token plaintext. Completing the
        // writer alone would have left these reachable from the singleton until
        // the process exited, which is not a disposition path.
        Assert.Equal(0, handoff.Queued);

        // And the discard attempted nothing: no transport, no terminal write,
        // no abandonment. Those rows are still Pending and belong to the sweep.
        Assert.Equal(0, writer.Closed);
    }

    // ------------------------------------------------------------------

    private static async Task RunAsync(
        BoundedNotificationHandoff? handoff,
        INotificationSweeper sweeper,
        NotificationSender? sender,
        TimeSpan? drain = null)
    {
        using var stopping = new CancellationTokenSource();

        var pump = new NotificationPump(
            sweeper,
            handoff: handoff,
            sender: sender,
            sweepInterval: Tick,
            shutdownDrain: drain ?? TimeSpan.FromSeconds(1));

        var running = pump.RunAsync(stopping.Token);

        // Long enough for the immediate sweep and any queued sends.
        await Task.Delay(Tick * 5);

        await stopping.CancelAsync();

        await running;
    }

    private static NotificationSender Sender(
        INotificationTransport transport,
        INotificationTerminalWriter writer)
        => new(
            TimeProvider.System,
            new AlwaysEligibleGate(),
            new NotificationTemplates(new Uri("https://app.example.com")),
            transport,
            writer);

    private static NotificationDeclaration Declaration()
        => new(
            NotificationId.New(),
            NotificationType.AccountActivation,
            UserTokenId.New(),
            "john.smith@example.com",
            "plaintext");

    private sealed class CountingSweeper : INotificationSweeper
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        public Task<int> SweepAsync(
            TimeSpan graceWindow, int batchSize, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(0);
        }
    }

    private sealed class AlwaysEligibleGate : INotificationGate
    {
        public Task<NotificationEligibility> EvaluateAsync(
            UserTokenId tokenId, CancellationToken cancellationToken)
            => Task.FromResult(NotificationEligibility.Eligible);
    }

    private sealed class RecordingTransport : INotificationTransport
    {
        private readonly List<RenderedMessage> _sent = [];

        internal IReadOnlyList<RenderedMessage> Sent
        {
            get { lock (_sent) { return _sent.ToArray(); } }
        }

        public Task<string?> SendAsync(
            RenderedMessage message, CancellationToken cancellationToken)
        {
            lock (_sent) { _sent.Add(message); }

            return Task.FromResult<string?>("ref");
        }
    }

    private sealed class SlowTransport(TimeSpan delay) : INotificationTransport
    {
        public async Task<string?> SendAsync(
            RenderedMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);

            return "ref";
        }
    }

    private sealed class CountingWriter : INotificationTerminalWriter
    {
        private int _closed;

        internal int Closed => Volatile.Read(ref _closed);

        public Task CloseAsync(
            NotificationId notificationId,
            NotificationOutcome outcome,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _closed);

            return Task.CompletedTask;
        }
    }
}
