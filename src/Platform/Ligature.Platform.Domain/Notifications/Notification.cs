using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Notifications;

/// <summary>
/// One row per token issued by USR-C1, CRD-C2 or CRD-C5: the record of what
/// happened when the system tried to deliver the secret.
///
/// It is NOT a work queue. Nothing dequeues it to send, because the only
/// process that ever holds the plaintext is the command that issued it
/// (D-NOTIF-01, D-NOTIF-02). The row is created Pending inside the issuing
/// transaction and closed exactly once — by the post-commit sender, or by the
/// sweeper if the sender was never observed to begin.
///
/// There is deliberately no MarkSent or MarkNotSent here. The terminal write is
/// a single guarded UPDATE on the sender's own pooled connection, outside every
/// ambient transaction (IMPL-N02), so it never travels through this type. This
/// class owns creation; the N9 trigger owns the transition.
///
/// No plaintext token, no rendered body, no URL, and no attempt counter. N16 is
/// structural, and the absence of a property here is half of what makes it so —
/// the other half is the fixed column list the writer maps to.
/// </summary>
public sealed class Notification : Entity<NotificationId>
{
    // EF Core materialization only.
    private Notification()
    {
    }

    private Notification(
        NotificationId id,
        NotificationType notificationType,
        UserTokenId tokenId,
        string recipient)
        : base(id)
    {
        NotificationType = notificationType;
        TokenId = tokenId;
        Recipient = recipient;
        Status = NotificationStatus.Pending;
    }

    public NotificationType NotificationType { get; }

    /// <summary>
    /// The issued token this record is about. UNIQUE (N2): at most one
    /// notification per issued token, which is D-NOTIF-01's send-once-or-never
    /// expressed as a constraint rather than as a rule.
    /// </summary>
    public UserTokenId TokenId { get; }

    /// <summary>
    /// The address as it stood at issuance. It answers "where did we attempt to
    /// send it", which a join to app_user.Email cannot answer once the address
    /// has changed. Personal data, disposed of by retention rather than
    /// anonymisation.
    /// </summary>
    public string Recipient { get; }

    public NotificationStatus Status { get; private set; }

    public NotSentReason? NotSentReason { get; private set; }

    /// <summary>
    /// When the sender BEGAN processing, taken before the eligibility gate so
    /// that a gate refusal carries it as well as a transport refusal. Null for
    /// Pending, and null for Abandoned — which is precisely what Abandoned
    /// means: no attempt was ever observed (N6).
    /// </summary>
    public DateTimeOffset? AttemptedAt { get; private set; }

    /// <summary>
    /// When the terminal update was issued. Present if and only if the row is
    /// no longer Pending (N5). Retention runs from here.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// The transport's acceptance reference, where the transport supplies one.
    /// The only evidence "transport accepted" can point at, and permitted only
    /// when Sent (N7); it may still be null when Sent, because not every
    /// transport returns one.
    /// </summary>
    public string? TransportMessageId { get; private set; }

    /// <summary>
    /// The issuing transaction's time, defaulted by the database rather than
    /// taken from the application clock (§3.2). The grace window runs from
    /// here and the sweeper compares it against the database's own now(), so
    /// both sides of that comparison come from one clock.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification CreatePending(
        NotificationId id,
        NotificationType notificationType,
        UserTokenId tokenId,
        string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            throw new DomainException(
                "A notification recipient cannot be empty.");
        }

        return new Notification(
            id,
            notificationType,
            tokenId,
            recipient);
    }
}
