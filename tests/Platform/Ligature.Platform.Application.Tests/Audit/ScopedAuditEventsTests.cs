using Ligature.Platform.Application.Audit;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// Every failure here is an emission defect and says so in the same words,
/// because "Audit emission defect" is the phrase an operator searches for
/// when a 500 has no detail (E1 decision: no dedicated exception type).
///
/// The collector's lifetime is a command, not a scope — the same distinction
/// the execution context draws for authority, and for the same reason: a
/// scope may dispatch several commands, and a declaration that outlived its
/// command would be written under the next one's operation.
/// </summary>
public sealed class ScopedAuditEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_declaration_outside_a_command_is_refused()
    {
        var events = new ScopedAuditEvents();

        var failure = Assert.Throws<InvalidOperationException>(() => events.Emit("UserCreated", 1));

        Assert.StartsWith("Audit emission defect", failure.Message);
        Assert.Contains("outside a dispatched command", failure.Message);
    }

    [Fact]
    public void Declarations_are_collected_for_the_open_command()
    {
        var events = new ScopedAuditEvents();
        var operationId = Guid.NewGuid();

        using (events.BeginCommand(operationId, Now))
        {
            var first = events.Emit("UserCreated", 1);
            var second = events.Emit("IdentityCreated", 1);

            Assert.Equal(operationId, events.OperationId);
            Assert.Equal(Now, events.OccurredAt);
            Assert.Equal([first, second], events.Declarations);
        }
    }

    [Fact]
    public void Closing_the_command_forgets_its_declarations_and_operation()
    {
        var events = new ScopedAuditEvents();

        using (events.BeginCommand(Guid.NewGuid(), Now))
        {
            events.Emit("UserCreated", 1);
        }

        Assert.Empty(events.Declarations);

        Assert.StartsWith(
            "Audit emission defect",
            Assert.Throws<InvalidOperationException>(() => events.OperationId).Message);
    }

    [Fact]
    public void A_later_command_in_the_same_scope_starts_clean()
    {
        var events = new ScopedAuditEvents();

        using (events.BeginCommand(Guid.NewGuid(), Now))
            events.Emit("UserCreated", 1);

        var second = Guid.NewGuid();

        using (events.BeginCommand(second, Now.AddMinutes(1)))
        {
            Assert.Empty(events.Declarations);
            Assert.Equal(second, events.OperationId);
        }
    }

    [Fact]
    public void Commands_cannot_be_nested()
    {
        var events = new ScopedAuditEvents();

        using var outer = events.BeginCommand(Guid.NewGuid(), Now);

        var failure = Assert.Throws<InvalidOperationException>(() => events.BeginCommand(Guid.NewGuid(), Now));

        Assert.StartsWith("Audit emission defect", failure.Message);
        Assert.Contains("not nested", failure.Message);
    }

    /// <summary>
    /// A retried unit of work replays the handler; the declarations of the
    /// attempt that rolled back must not be written with the one that commits.
    /// </summary>
    [Fact]
    public void Clearing_forgets_declarations_but_keeps_the_command_open()
    {
        var events = new ScopedAuditEvents();
        var operationId = Guid.NewGuid();

        using (events.BeginCommand(operationId, Now))
        {
            events.Emit("UserCreated", 1);
            events.ClearDeclarations();

            Assert.Empty(events.Declarations);
            Assert.Equal(operationId, events.OperationId);
        }
    }

    [Fact]
    public void A_stale_handle_does_not_close_a_later_command()
    {
        var events = new ScopedAuditEvents();

        var first = events.BeginCommand(Guid.NewGuid(), Now);
        first.Dispose();

        var second = Guid.NewGuid();
        using var _ = events.BeginCommand(second, Now);

        first.Dispose();

        Assert.Equal(second, events.OperationId);
    }
}
