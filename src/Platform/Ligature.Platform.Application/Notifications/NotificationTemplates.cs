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
/// Each page must therefore read window.location.hash and POST the token with
/// the new password: /activate to /api/account/activate, and /reset-password
/// to CRD-C3's endpoint when it lands. That contract is the frontend's half of
/// this design.
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

            NotificationType.PasswordReset =>
                PasswordReset(declaration),

            // AdminPasswordReset arrives with CRD-C5 and its own copy. A type
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

             {Link("/activate", declaration.PlaintextToken)}

             The link can only be used once, and it expires. If it has already
             expired, ask your administrator to send a new one.

             If you were not expecting this, you can ignore this message — the
             account cannot be used until the link is opened.
             """);

    /// <summary>
    /// CRD-C2's message.
    ///
    /// Written for a reader who did NOT ask for it, because that is the one
    /// who most needs it to be clear: anyone may type anyone's address into
    /// the form, so an unrequested reset mail is an ordinary event rather than
    /// evidence of an attack. It says plainly that ignoring it changes
    /// nothing, and it does not say who requested it — the system does not
    /// know, and guessing would be worse than silence.
    ///
    /// It also avoids implying the address is registered. The mail only
    /// reaches a mailbox whose account exists, so the fact is already
    /// disclosed to whoever reads it; what it must not do is confirm anything
    /// to someone who merely typed the address in.
    /// </summary>
    private RenderedMessage PasswordReset(NotificationDeclaration declaration)
        => new(
            declaration.Recipient,
            "Reset your password",
            $"""
             Someone asked for a password reset for your account.

             To choose a new password, open this link:

             {Link("/reset-password", declaration.PlaintextToken)}

             The link can only be used once, and it expires shortly. Asking
             again replaces it, so only the newest link will work.

             If you did not ask for this, you can ignore this message. Your
             password has not changed, and it will not change unless somebody
             opens the link above.
             """);

    /// <summary>
    /// AbsoluteUri, not ToString(): Uri.ToString() returns a partially
    /// UNESCAPED form, so a token containing URL syntax would produce a broken
    /// or truncated link. Today's tokens are "{guid}.{base64url}" and need no
    /// escaping, which is exactly why this would have gone unnoticed until the
    /// token format changed.
    ///
    /// The path is a parameter because each type has its own page, and the
    /// FRAGMENT is what carries the token in every case — see the class
    /// summary for why that is a security property rather than a style.
    /// </summary>
    private string Link(string path, string plaintextToken)
        => new UriBuilder(_publicBaseUrl)
        {
            Path = path,
            Fragment = $"token={Uri.EscapeDataString(plaintextToken)}",
        }.Uri.AbsoluteUri;
}
