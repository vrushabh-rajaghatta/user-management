using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// What an issuing command injects to declare that a token's plaintext must
/// reach its subject. Declaring is all it can do.
///
/// There is deliberately no way to send through this interface, no way to reach
/// a transport, and no way to observe what was declared. A handler that could
/// send could also forget to; a handler that can only declare leaves the rest
/// to the pipeline, which cannot omit itself.
///
/// This is NOT a general-purpose "send a notification" operation, and must not
/// become one. It is reachable only from the three issuing commands,
/// immediately after their own token issuance, with the token they just
/// created. A public entry point here would be a way to make the system mail
/// an arbitrary token to an arbitrary address.
///
/// The type/token agreement (N14) is CHECKED here as well, as the frozen
/// specification requires of the single writer: the caller passes the issued
/// token itself rather than its id, so the pairing is validated from what the
/// command holds, without a second read of user_token.
/// </summary>
public interface INotificationEvents
{
    /// <summary>
    /// Declares the notification for a token this command has just issued.
    /// </summary>
    /// <param name="token">
    /// The issued token. Only its id is recorded; its type is what N14 is
    /// checked against.
    /// </param>
    /// <param name="plaintextToken">
    /// Held in memory for the lifetime of this command and never persisted.
    /// The pipeline builds a row from the rest of this declaration; the
    /// plaintext travels no further than the post-commit sender.
    /// </param>
    void Emit(
        NotificationType notificationType,
        UserToken token,
        string recipient,
        string plaintextToken);
}
