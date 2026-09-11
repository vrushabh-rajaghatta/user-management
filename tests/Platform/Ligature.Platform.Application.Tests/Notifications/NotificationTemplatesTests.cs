using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// The rendered message is the one place the token plaintext is deliberately
/// written down, so where it is written down matters.
/// </summary>
public sealed class NotificationTemplatesTests
{
    private static readonly Uri BaseUrl = new("https://app.example.com");

    [Fact]
    public void The_token_travels_in_the_fragment_and_never_the_query()
    {
        var rendered = Render("plain-token-value");

        // A fragment is never sent to a server. A query string would put a
        // bearer credential into the activation page's access logs, its proxy
        // logs, and any Referer header it emits.
        Assert.Contains("#token=plain-token-value", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_link_points_at_the_activation_path_on_the_configured_origin()
    {
        var rendered = Render("plain-token-value");

        Assert.Contains(
            "https://app.example.com/activate#token=",
            rendered.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_containing_url_syntax_is_escaped()
    {
        // The token is "{guid}.{base64url}" today, which needs no escaping —
        // but the template must not depend on that, because a future token
        // format that did would silently produce a broken or truncated link.
        var rendered = Render("has spaces & ampersand#hash");

        Assert.DoesNotContain(
            "has spaces & ampersand#hash", rendered.Body, StringComparison.Ordinal);

        Assert.Contains("%20", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_is_addressed_to_the_recipient_snapshot()
    {
        var rendered = Render("token");

        Assert.Equal("john.smith@example.com", rendered.Recipient);
        Assert.False(string.IsNullOrWhiteSpace(rendered.Subject));
    }

    [Fact]
    public void A_type_with_no_template_is_a_defect_rather_than_a_fallback()
    {
        var templates = new NotificationTemplates(BaseUrl);

        // Both reset types arrive with slice N2. Sending the wrong message
        // about a credential would be worse than sending none.
        var failure = Assert.Throws<InvalidOperationException>(
            () => templates.Render(Declaration("token", NotificationType.PasswordReset)));

        Assert.Contains("no template", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RenderedMessage Render(string plaintext)
        => new NotificationTemplates(BaseUrl).Render(Declaration(plaintext));

    private static NotificationDeclaration Declaration(
        string plaintext,
        NotificationType type = NotificationType.AccountActivation)
        => new(
            NotificationId.New(),
            type,
            UserTokenId.New(),
            "john.smith@example.com",
            plaintext);
}
