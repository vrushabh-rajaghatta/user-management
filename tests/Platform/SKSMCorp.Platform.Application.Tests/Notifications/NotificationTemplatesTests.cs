using SKSMCorp.Platform.Application.Notifications;
using SKSMCorp.Platform.Domain.Notifications;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Tests.Notifications;

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

        // Every V1 type now has a template (AdminPasswordReset arrived with
        // CRD-C5), so an out-of-range value stands in for the next type added
        // without one. Sending the wrong message about a credential would be
        // worse than sending none.
        var failure = Assert.Throws<InvalidOperationException>(
            () => templates.Render(
                Declaration("token", (NotificationType)999)));

        Assert.Contains("no template", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CRD-C2's message. The link must point at the reset page, not the
    /// activation one, and must carry the token in the FRAGMENT — a query
    /// string would put a bearer credential into access and proxy logs.
    /// </summary>
    [Fact]
    public void The_password_reset_message_links_to_the_reset_page()
    {
        var rendered = new NotificationTemplates(BaseUrl)
            .Render(Declaration("a-plaintext-token", NotificationType.PasswordReset));

        Assert.Equal("Reset your password", rendered.Subject);
        Assert.Contains("/reset-password#token=a-plaintext-token", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("/activate", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", rendered.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// CRD-C5's message. Same page as CRD-C2's — the token type is the same and
    /// CRD-C3 consumes both — but it must say an administrator started it, so
    /// the reader can tell it apart from the forgotten-password form.
    /// </summary>
    [Fact]
    public void The_admin_password_reset_message_links_to_the_reset_page_and_says_who_started_it()
    {
        var templates = new NotificationTemplates(BaseUrl);

        var admin = templates.Render(
            Declaration("a-plaintext-token", NotificationType.AdminPasswordReset));

        var self = templates.Render(
            Declaration("a-plaintext-token", NotificationType.PasswordReset));

        Assert.Contains(
            "https://app.example.com/reset-password#token=a-plaintext-token",
            admin.Body,
            StringComparison.Ordinal);

        Assert.DoesNotContain("/activate", admin.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", admin.Body, StringComparison.Ordinal);
        Assert.Contains("administrator", admin.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("john.smith@example.com", admin.Recipient);

        Assert.NotEqual(self.Subject, admin.Subject);
        Assert.NotEqual(self.Body, admin.Body);
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
