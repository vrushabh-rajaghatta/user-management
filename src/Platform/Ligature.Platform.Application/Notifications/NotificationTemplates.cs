using Ligature.Platform.Domain.Notifications;

namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// Release-controlled message templates (IMPL-N04), keyed by type, in the same
/// sense the permission catalogue is release-owned: a tenant cannot edit them
/// in V1. Tenant-editable templates would be a feature with its own entity and
/// its own audit.
///
/// Rendering happens in memory immediately before transport and its output is
/// never persisted and never logged. The rendered body contains the activation
/// URL, and the URL contains the token plaintext — which is the whole reason
/// nothing downstream of the transport ever sees a RenderedMessage.
///
/// THE TOKEN TRAVELS IN THE URL FRAGMENT, not the query string. A fragment is
/// never sent to a server, so it cannot appear in access logs, proxy logs or a
/// Referer header on the activation page. The token is a bearer credential —
/// whoever holds it can set the password — so keeping it out of every log on
/// the path is worth the small awkwardness of reading it in the browser.
///
/// The activation page must therefore read window.location.hash and POST the
/// token with the new password to /api/account/activate. That contract is the
/// frontend's half of this design.
/// </summary>
internal sealed class NotificationTemplates
{
    private readonly Uri _publicBaseUrl;

    internal NotificationTemplates(Uri publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(publicBaseUrl);

        _publicBaseUrl = publicBaseUrl;
    }

    internal RenderedMessage Render(NotificationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.NotificationType switch
        {
            NotificationType.AccountActivation =>
                Activation(declaration),

            // Both reset types arrive with slice N2 and their own copy. A type
            // the templates do not know is a defect rather than a fallback:
            // sending the wrong message about a credential is worse than
            // sending none, and the row will record TransportFailed.
            _ => throw new InvalidOperationException(
                $"Notification defect — no template for '{declaration.NotificationType}'. "
                + "The type set is release-controlled and a template lands with "
                + "the slice that introduces its type."),
        };
    }

    private RenderedMessage Activation(NotificationDeclaration declaration)
        => new(
            declaration.Recipient,
            "Activate your account",
            $"""
             Someone has created an account for you.

             To choose your password and finish setting it up, open this link:

             {Link(declaration.PlaintextToken)}

             The link can only be used once, and it expires. If it has already
             expired, ask your administrator to send a new one.

             If you were not expecting this, you can ignore this message — the
             account cannot be used until the link is opened.
             """);

    /// <summary>
    /// AbsoluteUri, not ToString(): Uri.ToString() returns a partially
    /// UNESCAPED form, so a token containing URL syntax would produce a broken
    /// or truncated link. Today's tokens are "{guid}.{base64url}" and need no
    /// escaping, which is exactly why this would have gone unnoticed until the
    /// token format changed.
    /// </summary>
    private string Link(string plaintextToken)
        => new UriBuilder(_publicBaseUrl)
        {
            Path = "/activate",
            Fragment = $"token={Uri.EscapeDataString(plaintextToken)}",
        }.Uri.AbsoluteUri;
}
