using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Domain.Notifications;

/// <summary>
/// N14 — a notification's type agrees with the type of the token it carries.
///
/// The three legal pairings, and nothing else:
///
///   AccountActivation   ↔ Activation
///   PasswordReset       ↔ PasswordReset
///   AdminPasswordReset  ↔ PasswordReset
///
/// The two reset messages share one token type because they deliver the same
/// capability — CRD-C3 consumes both — and differ only in what the message
/// says about who asked (UM §6.5). An activation message carrying a reset
/// token, or the reverse, would invite a user to one flow with a link that
/// only works in the other.
///
/// A cross-entity invariant with no database constraint by design: a
/// composite foreign key would need a new unique index on user_token, a User
/// Management schema change the Notification context does not justify. It is
/// enforced in the single writer instead (Notification spec N14).
///
/// A switch with no default arm that permits, so a type added to the release
/// set is refused until someone decides which token it carries.
/// </summary>
public static class NotificationTokenAgreement
{
    public static bool Permits(NotificationType notificationType, TokenType tokenType)
        => notificationType switch
        {
            NotificationType.AccountActivation =>
                tokenType == TokenType.Activation,

            NotificationType.PasswordReset or NotificationType.AdminPasswordReset =>
                tokenType == TokenType.PasswordReset,

            _ => false,
        };
}
