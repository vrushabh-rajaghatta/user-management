using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// Two structural protections for the token plaintext.
///
/// Neither guards against anything the code does today. Both make a future
/// mistake impossible rather than merely discouraged, which is how every other
/// secret-handling rule in this system is argued.
/// </summary>
public sealed class SecretRedactionTests
{
    private const string Secret = "the-live-activation-token";

    [Fact]
    public void A_declaration_does_not_print_its_token()
    {
        var declaration = new NotificationDeclaration(
            NotificationId.New(),
            NotificationType.AccountActivation,
            UserTokenId.New(),
            "john.smith@example.com",
            Secret);

        // A record's generated ToString prints every property. One day's
        // $"{declaration}" in a log line, an exception message or an assertion
        // failure would otherwise publish a live credential.
        Assert.DoesNotContain(Secret, declaration.ToString(), StringComparison.Ordinal);

        // Nor the recipient, which is personal data.
        Assert.DoesNotContain(
            "john.smith@example.com", declaration.ToString(), StringComparison.Ordinal);

        // Still identifies the row, which is what a diagnostic actually needs.
        Assert.Contains(
            declaration.NotificationId.Value.ToString(),
            declaration.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rendered_message_does_not_print_its_body()
    {
        var message = new RenderedMessage(
            "john.smith@example.com",
            "Activate your account",
            $"open https://app.example.com/activate#token={Secret}");

        Assert.DoesNotContain(Secret, message.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Interpolating_a_declaration_cannot_leak_the_token()
    {
        var declaration = new NotificationDeclaration(
            NotificationId.New(),
            NotificationType.AccountActivation,
            UserTokenId.New(),
            "john.smith@example.com",
            Secret);

        // The shape a future diagnostic would most plausibly take — and the
        // shape AuditCommandScopeBehavior already uses for its own failures.
        var diagnostic = $"failed to hand off {declaration}";

        Assert.DoesNotContain(Secret, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_mail_configured_there_is_no_handoff_to_put_a_plaintext_in()
    {
        var services = new ServiceCollection().AddPlatformApplication();

        using var provider = services.BuildServiceProvider();

        // The invariant behind coupling phases C and D. A queue registered
        // without the consumer that drains it would hold live activation
        // secrets in a singleton until the process exited — strictly worse than
        // the state it replaced, where the declaration died with its command.
        Assert.Null(provider.GetService<INotificationHandoff>());
        Assert.Null(provider.GetService<BoundedNotificationHandoff>());
    }

    [Fact]
    public void With_mail_configured_the_handoff_and_its_consumer_arrive_together()
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddNotificationDelivery(new Uri("https://app.example.com"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<INotificationHandoff>());
    }
}
