using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// The collector's command discipline.
///
/// A declaration holds a live activation secret, so "whose command does this
/// belong to" is a security question here rather than only a bookkeeping one.
/// A declaration that outlived its command would attach that secret to the next
/// command dispatched on the same scope.
/// </summary>
public sealed class ScopedNotificationEventsTests
{
    [Fact]
    public void A_declaration_outside_a_dispatched_command_is_refused()
    {
        var collector = new ScopedNotificationEvents();

        var failure = Assert.Throws<InvalidOperationException>(
            () => Declare(collector));

        // Nothing would ever drain it, so the plaintext would sit in a
        // collector no one reads.
        Assert.Contains("outside a dispatched command", failure.Message);
    }

    [Fact]
    public void A_declaration_is_visible_to_the_pipeline_while_the_command_is_open()
    {
        var collector = new ScopedNotificationEvents();

        using var open = ((INotificationEmissionScope)collector).BeginCommand();

        Declare(collector);

        var declarations = ((INotificationEmissionScope)collector).Declarations;

        Assert.Single(declarations);
        Assert.Equal(NotificationType.AccountActivation, declarations[0].NotificationType);
        Assert.Equal("john.smith@example.com", declarations[0].Recipient);
        Assert.Equal("plaintext-token", declarations[0].PlaintextToken);
        Assert.NotEqual(Guid.Empty, declarations[0].NotificationId.Value);
    }

    [Fact]
    public void Each_declaration_gets_its_own_notification_identity()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        using var open = scope.BeginCommand();

        Declare(collector);
        Declare(collector);

        Assert.NotEqual(
            scope.Declarations[0].NotificationId.Value,
            scope.Declarations[1].NotificationId.Value);
    }

    [Fact]
    public void Clearing_forgets_the_attempt_that_rolled_back()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        using var open = scope.BeginCommand();

        Declare(collector);
        scope.ClearDeclarations();

        Assert.Empty(scope.Declarations);
    }

    [Fact]
    public void A_declaration_does_not_outlive_its_command()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        var open = scope.BeginCommand();
        Declare(collector);
        open.Dispose();

        // The reference to the plaintext is gone with it. This is the whole of
        // what "discard" means for a CLR string: no live reference, eligible
        // for collection. It is not memory erasure and does not claim to be.
        Assert.Empty(scope.Declarations);
    }

    [Fact]
    public void A_second_command_does_not_inherit_the_first_commands_declaration()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        using (scope.BeginCommand())
        {
            Declare(collector);
        }

        using var second = scope.BeginCommand();

        Assert.Empty(scope.Declarations);
    }

    [Fact]
    public void A_nested_dispatch_is_refused()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        using var open = scope.BeginCommand();

        var failure = Assert.Throws<InvalidOperationException>(
            () => scope.BeginCommand());

        Assert.Contains("already open", failure.Message);
    }

    [Fact]
    public void A_stale_lifetime_handle_does_not_close_a_later_command()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        var first = scope.BeginCommand();
        first.Dispose();

        using var second = scope.BeginCommand();
        Declare(collector);

        // Disposing the first command's handle a second time must not clear
        // the command that is now open.
        first.Dispose();

        Assert.Single(scope.Declarations);
    }

    [Fact]
    public void A_declaration_after_its_command_closed_is_refused()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;

        scope.BeginCommand().Dispose();

        Assert.Throws<InvalidOperationException>(() => Declare(collector));
    }

    private static void Declare(INotificationEvents events)
        => events.Emit(
            NotificationType.AccountActivation,
            UserTokenId.New(),
            "john.smith@example.com",
            "plaintext-token");
}
