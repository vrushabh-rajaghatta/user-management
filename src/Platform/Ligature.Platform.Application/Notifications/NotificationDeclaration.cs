using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// One declared notification, for the lifetime of the command that declared it.
///
/// This is the ONLY object in the system that holds both the notification's
/// identity and the token plaintext, and it exists in memory only: nothing
/// serialises it, nothing persists it, and the entity the pipeline builds from
/// it has no field the plaintext could occupy (N16).
///
/// The identifier is minted here rather than by the writer, because the
/// post-commit sender needs to name the row it is closing and the row and the
/// declaration must agree on which notification they are.
/// </summary>
internal sealed record NotificationDeclaration(
    NotificationId NotificationId,
    NotificationType NotificationType,
    UserTokenId TokenId,
    string Recipient,
    string PlaintextToken);
