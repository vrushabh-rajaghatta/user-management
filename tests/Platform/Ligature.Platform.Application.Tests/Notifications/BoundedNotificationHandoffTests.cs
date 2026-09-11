using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Notifications;

/// <summary>
/// Shedding is the design, not failure handling. Every queued item holds a live
/// activation secret, so capacity is a bound on resident plaintexts as much as
/// a bound on backlog.
/// </summary>
public sealed class BoundedNotificationHandoffTests
{
    [Fact]
    public void The_handoff_accepts_up_to_capacity()
    {
        var handoff = new BoundedNotificationHandoff();

        for (var i = 0; i < NotificationConstants.HandoffCapacity; i++)
        {
            Assert.True(handoff.TryHandoff(Declaration()));
        }
    }

    [Fact]
    public void Beyond_capacity_it_sheds_rather_than_blocking_or_throwing()
    {
        var handoff = new BoundedNotificationHandoff();

        for (var i = 0; i < NotificationConstants.HandoffCapacity; i++)
        {
            handoff.TryHandoff(Declaration());
        }

        // Not an exception and not a wait. A command whose user, identity and
        // token are already committed must not be failed or delayed by the
        // sender's backlog — the row simply stays Pending for the sweeper.
        Assert.False(handoff.TryHandoff(Declaration()));
    }

    [Fact]
    public void Shedding_drops_the_new_item_and_keeps_what_is_queued()
    {
        var handoff = new BoundedNotificationHandoff();

        var first = Declaration();

        handoff.TryHandoff(first);

        for (var i = 0; i < NotificationConstants.HandoffCapacity; i++)
        {
            handoff.TryHandoff(Declaration());
        }

        handoff.Complete();

        // DropWrite, not DropOldest: discarding the oldest activation secret to
        // make room for a newer one would be no better and harder to reason
        // about. The first item queued is still the first item read.
        var drained = Drain(handoff);

        Assert.Equal(NotificationConstants.HandoffCapacity, drained.Count);
        Assert.Same(first, drained[0]);
    }

    [Fact]
    public void A_completed_handoff_refuses_further_work()
    {
        var handoff = new BoundedNotificationHandoff();

        handoff.Complete();

        // What the host does when it begins stopping: no new work is accepted,
        // so a command committing during shutdown does not hand off something
        // nothing will drain.
        Assert.False(handoff.TryHandoff(Declaration()));
    }

    [Fact]
    public void Completing_lets_a_reader_finish_what_is_already_queued()
    {
        var handoff = new BoundedNotificationHandoff();

        handoff.TryHandoff(Declaration());
        handoff.TryHandoff(Declaration());
        handoff.Complete();

        // The bounded drain depends on this: completing ends the read loop
        // naturally once the backlog is gone, rather than cancelling it.
        Assert.Equal(2, Drain(handoff).Count);
    }

    private static List<NotificationDeclaration> Drain(
        BoundedNotificationHandoff handoff)
    {
        var drained = new List<NotificationDeclaration>();

        var reader = handoff.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();

        try
        {
            while (reader.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                drained.Add(reader.Current);
            }
        }
        finally
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return drained;
    }

    private static NotificationDeclaration Declaration()
        => new(
            NotificationId.New(),
            NotificationType.AccountActivation,
            UserTokenId.New(),
            "john.smith@example.com",
            "plaintext");
}
