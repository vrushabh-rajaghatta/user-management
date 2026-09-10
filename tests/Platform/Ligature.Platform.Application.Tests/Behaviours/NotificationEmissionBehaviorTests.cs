using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Tests.Behaviors;

/// <summary>
/// Per-attempt declaration isolation — the invariant that makes a replayed
/// unit of work safe for a secret-bearing declaration.
///
/// The behaviour sits inside the execution strategy's delegate, so a retry
/// re-invokes it. What must be true is that each attempt sees only its own
/// declarations: the attempt that rolled back declared a notification for a
/// token that rolled back with it, and writing that alongside the attempt that
/// committed would enqueue a live plaintext for a token which no longer
/// exists.
///
/// Deliberately at the behaviour level rather than end to end. Proving it
/// through a real replayed transaction additionally requires EF to reset its
/// change tracker between attempts, which it does not do — a platform-level
/// defect recorded separately in docs/requirements.md, and emphatically not
/// something the notification path should work around.
/// </summary>
public sealed class NotificationEmissionBehaviorTests
{
    [Fact]
    public async Task Each_attempt_persists_only_its_own_declarations()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;
        var repository = new RecordingNotificationRepository();

        var behavior =
            new NotificationEmissionBehavior<FakeCommand, FakeResult>(
                scope, repository);

        using var open = scope.BeginCommand();

        var first = UserTokenId.New();
        var second = UserTokenId.New();

        // Attempt 1 — the handler declares, the behaviour writes, and the unit
        // of work then fails somewhere beyond this behaviour.
        await behavior.Handle(
            new FakeCommand(),
            CancellationToken.None,
            _ => Declare(collector, first, "plaintext-A"));

        var afterFirstAttempt = repository.Added.Count;

        // Attempt 2 — the strategy replays the whole unit, so this behaviour
        // runs again on the same scope and the same collector.
        await behavior.Handle(
            new FakeCommand(),
            CancellationToken.None,
            _ => Declare(collector, second, "plaintext-B"));

        var secondAttempt = repository.Added.Skip(afterFirstAttempt).ToArray();

        Assert.Equal(1, afterFirstAttempt);
        Assert.Equal(first, repository.Added[0].TokenId);

        // The whole point: ONE, not two. Without the per-attempt clear the
        // replay would carry the rolled-back attempt's declaration forward and
        // write both.
        Assert.Single(secondAttempt);
        Assert.Equal(second, secondAttempt[0].TokenId);
    }

    [Fact]
    public async Task A_command_that_declares_nothing_writes_nothing()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;
        var repository = new RecordingNotificationRepository();

        var behavior =
            new NotificationEmissionBehavior<FakeCommand, FakeResult>(
                scope, repository);

        using var open = scope.BeginCommand();

        await behavior.Handle(
            new FakeCommand(),
            CancellationToken.None,
            _ => Task.FromResult(new FakeResult()));

        // Every command that issues no token, which is most of them. The
        // pipeline does not invent a row.
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task The_row_carries_the_declaration_and_starts_pending()
    {
        var collector = new ScopedNotificationEvents();
        var scope = (INotificationEmissionScope)collector;
        var repository = new RecordingNotificationRepository();

        var behavior =
            new NotificationEmissionBehavior<FakeCommand, FakeResult>(
                scope, repository);

        using var open = scope.BeginCommand();

        var tokenId = UserTokenId.New();

        await behavior.Handle(
            new FakeCommand(),
            CancellationToken.None,
            _ => Declare(collector, tokenId, "plaintext"));

        var written = Assert.Single(repository.Added);

        Assert.Equal(NotificationType.AccountActivation, written.NotificationType);
        Assert.Equal(tokenId, written.TokenId);
        Assert.Equal("john.smith@example.com", written.Recipient);
        Assert.Equal(NotificationStatus.Pending, written.Status);
        Assert.Null(written.NotSentReason);
        Assert.Null(written.AttemptedAt);
        Assert.Null(written.ClosedAt);
        Assert.Null(written.TransportMessageId);
    }

    private static Task<FakeResult> Declare(
        INotificationEvents events,
        UserTokenId tokenId,
        string plaintext)
    {
        events.Emit(
            NotificationType.AccountActivation,
            tokenId,
            "john.smith@example.com",
            plaintext);

        return Task.FromResult(new FakeResult());
    }

    private sealed record FakeResult;

    private sealed record FakeCommand : ICommand<FakeResult>;

    private sealed class RecordingNotificationRepository : INotificationRepository
    {
        internal List<Notification> Added { get; } = [];

        public Task AddAsync(
            Notification notification,
            CancellationToken cancellationToken)
        {
            Added.Add(notification);

            return Task.CompletedTask;
        }
    }
}
