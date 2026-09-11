using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// The outcome of the eligibility predicate. Maps one-to-one onto the terminal
/// reasons the gate can produce; transport failures are not its business.
/// </summary>
internal enum NotificationEligibility
{
    Eligible,
    TokenNotLive,
    SubjectInactive,
}

/// <summary>
/// The eligibility gate (§5.2, N15): is this token still worth mailing?
///
///     token live      UsedAt IS NULL AND InvalidatedAt IS NULL AND ExpiresAt > now()
///     subject active  identity Active AND user Active
///
/// Evaluated immediately before transport, in one short read, never earlier and
/// never cached. A token can be invalidated by a concurrent issuance between
/// the row being written and the send being attempted, and the whole point of
/// reading here is to notice.
///
/// When both conditions fail the answer is TokenNotLive: the token condition is
/// evaluated first, and it is the one that makes the message useless rather
/// than merely inappropriate.
///
/// The two subject conditions are Notification's own. User Management is silent
/// on tokens in the deactivation cascade, so a token can be live for an
/// inactive user; sending would be harmless — sign-in fails on identity status
/// — but a mail inviting a deactivated person to activate an account is a
/// support ticket and a confusing artefact in an investigation.
/// </summary>
internal interface INotificationGate
{
    Task<NotificationEligibility> EvaluateAsync(
        UserTokenId tokenId,
        CancellationToken cancellationToken);
}
