namespace Ligature.Platform.Domain.Notifications;

/// <summary>
/// Why a notification ended without being sent. Mandatory and write-once when
/// Status is NotSent, absent otherwise (N4).
///
/// TransportFailed and Abandoned are kept apart for the reason UM §6.5 keeps
/// Used and Invalidated apart: one means we know it did not go, the other that
/// we do not know. Abandoned includes the case where the transport had in fact
/// accepted the message and the process died before the outcome was recorded —
/// the honest record, since the system did not observe it.
/// </summary>
public enum NotSentReason
{
    TokenNotLive,
    SubjectInactive,
    TransportFailed,
    Abandoned
}
