namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// A message rendered in memory, immediately before it is handed to the
/// transport and never afterwards. The body contains the activation URL and
/// therefore the token plaintext, which is why nothing persists, logs or
/// returns this type.
/// </summary>
internal sealed record RenderedMessage(
    string Recipient,
    string Subject,
    string Body)
{
    /// <summary>
    /// The body carries the activation URL and therefore the token, so the
    /// generated ToString a record would otherwise supply is replaced by one
    /// that carries neither it nor the recipient.
    /// </summary>
    public override string ToString() => "RenderedMessage { redacted }";
}

/// <summary>
/// The one seam a mail provider plugs into. Everything provider-specific —
/// endpoints, OAuth, JWT assertions, credentials, HTTP — lives behind this and
/// never leaks into the Application assembly. Swapping Google for Microsoft is
/// a second implementation and a configuration change; no caller learns of it.
///
/// Accepting means the provider took responsibility for sending. It is NOT a
/// claim about receipt: delivery to the mailbox is unobservable in V1, and the
/// lifecycle says Sent rather than Delivered for exactly that reason.
///
/// Implementations THROW on refusal. A 4xx, a timeout and a socket failure are
/// all the same fact to the sender — we know it did not go — and all become
/// NotSent/TransportFailed. There is no retry: a retry needs the plaintext, and
/// keeping the plaintext long enough to retry is keeping it.
/// </summary>
internal interface INotificationTransport
{
    /// <returns>
    /// The provider's acceptance reference where it supplies one, otherwise
    /// null. It is the only evidence "the transport accepted" can point at, and
    /// N7 permits it to be recorded only on a Sent row.
    /// </returns>
    Task<string?> SendAsync(
        RenderedMessage message,
        CancellationToken cancellationToken);
}
