namespace Ligature.Platform.Domain.Notifications;

/// <summary>
/// The V1 type set (N3), release-controlled: widened by migration, never
/// narrowed while rows reference a value.
///
/// AdminPasswordReset is distinct from PasswordReset because the message says
/// something different, and UM §6.5 already keeps admin-initiated and
/// self-service resets apart in forensics. N-O4 is Product confirming that
/// distinction, not deciding it.
///
/// Every V1 type is secret-bearing: its message carries a token plaintext, and
/// the send-once-or-never rules apply to all three. A future non-secret type
/// arrives as a new value with its own retryable semantics, never as a mode of
/// this one.
/// </summary>
public enum NotificationType
{
    AccountActivation,
    PasswordReset,
    AdminPasswordReset
}
