using Ligature.Platform.Domain.Notifications;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// NOT-P2 for one declaration: stamp, gate, render, send, close, discard.
///
/// Registered only when mail is configured. Its absence is what puts the pump
/// into sweep-only mode, which is why this type is the optional one rather than
/// the transport: a sender that could not send would be a sender in name only.
///
/// It NEVER retries the transport, re-issues a token, writes to user_token
/// beyond the gate read, or delays anything a caller is waiting on. Recovery
/// from a failed send is a deliberate new issuance by a person — which shows up
/// in the audit trail as a second issuance rather than as a silent retry.
///
/// ON THE CLOCK. This takes TimeProvider rather than the repository's IClock,
/// because IClock is registered scoped and this is resolved by a singleton
/// pump; taking a scoped dependency here would fail validateScopes at startup.
/// The alternative — changing IClock's lifetime — would reach outside this
/// slice for a reason that is entirely local to it.
/// </summary>
internal sealed class NotificationSender
{
    private readonly TimeProvider _time;
    private readonly INotificationGate _gate;
    private readonly NotificationTemplates _templates;
    private readonly INotificationTransport _transport;
    private readonly INotificationTerminalWriter _writer;

    internal NotificationSender(
        TimeProvider time,
        INotificationGate gate,
        NotificationTemplates templates,
        INotificationTransport transport,
        INotificationTerminalWriter writer)
    {
        _time = time;
        _gate = gate;
        _templates = templates;
        _transport = transport;
        _writer = writer;
    }

    internal async Task SendAsync(
        NotificationDeclaration declaration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        // BEFORE the gate, not after. AttemptedAt means "the sender began
        // processing", so a gate refusal carries one too — and the span from
        // here to ClosedAt is what the N-O1 measurement reads, which would
        // silently exclude the gate read if this moved below it.
        var attemptedAt = _time.GetUtcNow();

        var eligibility = await _gate.EvaluateAsync(
            declaration.TokenId, cancellationToken);

        if (eligibility is not NotificationEligibility.Eligible)
        {
            await CloseAsync(
                declaration,
                new NotificationOutcome(
                    NotificationStatus.NotSent,
                    eligibility is NotificationEligibility.TokenNotLive
                        ? NotSentReason.TokenNotLive
                        : NotSentReason.SubjectInactive,
                    attemptedAt,
                    TransportMessageId: null),
                cancellationToken);

            return;
        }

        string? transportMessageId;

        try
        {
            // The rendered message holds the activation URL and therefore the
            // plaintext. It exists for exactly this call and is never returned,
            // stored or logged.
            transportMessageId = await _transport.SendAsync(
                _templates.Render(declaration), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // WE ARE STOPPING, and that is a different fact from a failure.
            // Cancelling an HTTP request does not un-send it: the provider may
            // already have accepted the message. Recording TransportFailed here
            // would claim we know it did not go, which we do not.
            //
            // So nothing is written. The row stays Pending and the sweeper
            // closes it as Abandoned — "the outcome was never observed" — which
            // is the one terminal reason that is true in this case, and exactly
            // the same shape as the sent-then-crashed case.
            //
            // A transport TIMEOUT is deliberately not this branch: the frozen
            // design assigns it to TransportFailed, and it arrives as a
            // cancellation of the transport's own token rather than ours.
            throw;
        }
        catch (Exception)
        {
            // A refusal, a timeout and a socket failure are one fact as far as
            // the model is concerned: we know it did not go. The exception is
            // deliberately not rethrown — this is an expected terminal outcome,
            // not a defect, and the row is where it gets recorded.
            await CloseAsync(
                declaration,
                new NotificationOutcome(
                    NotificationStatus.NotSent,
                    NotSentReason.TransportFailed,
                    attemptedAt,
                    TransportMessageId: null),
                cancellationToken);

            return;
        }

        await CloseAsync(
            declaration,
            new NotificationOutcome(
                NotificationStatus.Sent,
                Reason: null,
                attemptedAt,
                transportMessageId),
            cancellationToken);
    }

    /// <summary>
    /// If this throws, the row stays Pending and the sweeper closes it as
    /// Abandoned — the honest record, because the outcome was never persisted.
    /// That is also the sent-then-crashed case: the mail went, nobody wrote it
    /// down, and the system does not claim to know.
    /// </summary>
    private async Task CloseAsync(
        NotificationDeclaration declaration,
        NotificationOutcome outcome,
        CancellationToken cancellationToken)
        => await _writer.CloseAsync(
            declaration.NotificationId, outcome, cancellationToken);
}
