using Ligature.Platform.Persistence.Notifications;

namespace Ligature.Host.Configuration;

/// <summary>
/// How the host delivers mail, when it delivers any: one transport, and the
/// public base URL every link is built from.
/// </summary>
public abstract record MailDelivery(Uri PublicBaseUrl);

/// <summary>The production transport: the Gmail API.</summary>
public sealed record GmailDelivery(MailSettings Settings, Uri PublicBaseUrl) : MailDelivery(PublicBaseUrl);

/// <summary>
/// Development only (docs/architecture.md §8): each message written to a file in
/// <paramref name="Directory"/> instead of being sent.
/// </summary>
public sealed record DevelopmentSinkDelivery(string Directory, Uri PublicBaseUrl) : MailDelivery(PublicBaseUrl);
