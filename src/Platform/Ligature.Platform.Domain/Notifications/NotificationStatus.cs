namespace Ligature.Platform.Domain.Notifications;

/// <summary>
/// Sent and NotSent are terminal (N9). There is no Pending → Pending update, no
/// retry edge, and no edge out of a terminal state.
///
/// Sent means the transport accepted the message for sending. Delivery to the
/// recipient is not observable in V1 and is not claimed.
/// </summary>
public enum NotificationStatus
{
    Pending,
    Sent,
    NotSent
}
