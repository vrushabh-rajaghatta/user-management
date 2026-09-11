using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// NOT-P2's four sender-reachable outcomes, and the ordering property the
/// N-O1 measurement depends on.
///
/// Abandoned is absent on purpose: it is the sweeper's, and it means precisely
/// that the sender was never observed — so a sender that could produce it would
/// be contradicting the word.
/// </summary>
public sealed class NotificationSenderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 9, 14, 3, TimeSpan.Zero);

    [Fact]
    public async Task An_eligible_notification_is_sent_and_recorded_with_its_reference()
    {
        var writer = new RecordingWriter();

        await Send(writer, transport: new StubTransport("provider-ref"));

        var outcome = Assert.Single(writer.Closed).Outcome;

        Assert.Equal(NotificationStatus.Sent, outcome.Status);
        Assert.Null(outcome.Reason);
        Assert.Equal(Now, outcome.AttemptedAt);
        Assert.Equal("provider-ref", outcome.TransportMessageId);
    }

    [Fact]
    public async Task A_transport_that_returns_no_reference_still_records_sent()
    {
        var writer = new RecordingWriter();

        await Send(writer, transport: new StubTransport(messageId: null));

        var outcome = Assert.Single(writer.Closed).Outcome;

        // N7 permits a Sent row with no reference: not every provider gives
        // one, and the absence of evidence is not a failure to send.
        Assert.Equal(NotificationStatus.Sent, outcome.Status);
        Assert.Null(outcome.TransportMessageId);
    }

    [Fact]
    public async Task A_dead_token_closes_the_row_without_touching_the_transport()
        => await GateRefusal(
            NotificationEligibility.TokenNotLive, NotSentReason.TokenNotLive);

    [Fact]
    public async Task An_inactive_subject_closes_the_row_without_touching_the_transport()
        => await GateRefusal(
            NotificationEligibility.SubjectInactive, NotSentReason.SubjectInactive);

    private static async Task GateRefusal(
        NotificationEligibility refusal, NotSentReason expected)
    {
        var writer = new RecordingWriter();
        var transport = new StubTransport("unused");

        await Send(writer, transport, eligibility: refusal);

        var outcome = Assert.Single(writer.Closed).Outcome;

        Assert.Equal(NotificationStatus.NotSent, outcome.Status);
        Assert.Equal(expected, outcome.Reason);
        Assert.Null(outcome.TransportMessageId);

        // Nothing was handed to the transport, so nothing rendered the token.
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task A_gate_refusal_still_carries_an_attempt_time()
    {
        var writer = new RecordingWriter();

        await Send(
            writer,
            new StubTransport("unused"),
            eligibility: NotificationEligibility.TokenNotLive);

        // AttemptedAt means "the sender began processing", which it did. Only
        // Abandoned lacks one (N6), and this is not that.
        Assert.Equal(Now, Assert.Single(writer.Closed).Outcome.AttemptedAt);
    }

    [Fact]
    public async Task The_attempt_is_stamped_before_the_gate_is_read()
    {
        var writer = new RecordingWriter();
        var clock = new SteppingClock(Now, TimeSpan.FromSeconds(1));

        var gate = new StubGate(NotificationEligibility.Eligible, clock);

        await Send(writer, new StubTransport("ref"), gate: gate, time: clock);

        // The span from AttemptedAt to ClosedAt is what the N-O1 measurement
        // reads. Stamping after the gate would silently exclude the gate read
        // from every measured send.
        Assert.True(
            Assert.Single(writer.Closed).Outcome.AttemptedAt < gate.ReadAt,
            "AttemptedAt must precede the gate read.");
    }

    [Fact]
    public async Task A_transport_failure_is_recorded_rather_than_thrown()
    {
        var writer = new RecordingWriter();

        await Send(writer, new ThrowingTransport());

        var outcome = Assert.Single(writer.Closed).Outcome;

        // A refusal, a timeout and a socket failure are one fact: we know it
        // did not go. It is a terminal outcome, not a defect, so the row
        // records it and the consumer carries on.
        Assert.Equal(NotificationStatus.NotSent, outcome.Status);
        Assert.Equal(NotSentReason.TransportFailed, outcome.Reason);
        Assert.Null(outcome.TransportMessageId);
    }

    [Fact]
    public async Task The_transport_never_sees_a_second_attempt()
    {
        var writer = new RecordingWriter();
        var transport = new ThrowingTransport();

        await Send(writer, transport);

        // No retry, anywhere. A retry needs the plaintext, and keeping the
        // plaintext long enough to retry is keeping it.
        Assert.Equal(1, transport.Attempts);
    }

    // ------------------------------------------------------------------

    private static async Task Send(
        RecordingWriter writer,
        INotificationTransport transport,
        NotificationEligibility eligibility = NotificationEligibility.Eligible,
        StubGate? gate = null,
        TimeProvider? time = null)
    {
        var clock = time ?? new FixedClock(Now);

        var sender = new NotificationSender(
            clock,
            gate ?? new StubGate(eligibility, clock),
            new NotificationTemplates(new Uri("https://app.example.com")),
            transport,
            writer);

        await sender.SendAsync(
            new NotificationDeclaration(
                NotificationId.New(),
                NotificationType.AccountActivation,
                UserTokenId.New(),
                "john.smith@example.com",
                "plaintext"),
            CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Advances by a fixed step on every read, so ordering is visible.</summary>
    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            var value = _now;
            _now = _now.Add(step);
            return value;
        }
    }

    private sealed class StubGate(
        NotificationEligibility eligibility,
        TimeProvider time) : INotificationGate
    {
        internal DateTimeOffset ReadAt { get; private set; }

        public Task<NotificationEligibility> EvaluateAsync(
            UserTokenId tokenId, CancellationToken cancellationToken)
        {
            ReadAt = time.GetUtcNow();

            return Task.FromResult(eligibility);
        }
    }

    private sealed class StubTransport(string? messageId) : INotificationTransport
    {
        internal List<RenderedMessage> Sent { get; } = [];

        public Task<string?> SendAsync(
            RenderedMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);

            return Task.FromResult(messageId);
        }
    }

    private sealed class ThrowingTransport : INotificationTransport
    {
        internal int Attempts { get; private set; }

        public Task<string?> SendAsync(
            RenderedMessage message, CancellationToken cancellationToken)
        {
            Attempts++;

            throw new InvalidOperationException("provider refused");
        }
    }

    private sealed class RecordingWriter : INotificationTerminalWriter
    {
        internal List<(NotificationId Id, NotificationOutcome Outcome)> Closed { get; } = [];

        public Task CloseAsync(
            NotificationId notificationId,
            NotificationOutcome outcome,
            CancellationToken cancellationToken)
        {
            Closed.Add((notificationId, outcome));

            return Task.CompletedTask;
        }
    }
}
